using PeachPDF.PdfSharpCore.Pdf;
using PeachPDF.PdfSharpCore.Pdf.Advanced;
using System;
using System.Collections.Generic;

namespace PeachPDF.Tests.PdfSharpCoreTests.Pdf
{
    public class PdfDictionaryElementsTests
    {
        [Fact]
        public void Boolean_SetAndGet_RoundTrips()
        {
            var dict = new PdfDictionary();
            dict.Elements.SetBoolean("/Flag", true);

            Assert.True(dict.Elements.GetBoolean("/Flag"));
            Assert.True(dict.Elements.TryGetBoolean("/Flag", out var value));
            Assert.True(value);
        }

        [Fact]
        public void Boolean_MissingKey_ReturnsFalse()
        {
            var dict = new PdfDictionary();

            Assert.False(dict.Elements.GetBoolean("/Missing"));
            Assert.False(dict.Elements.TryGetBoolean("/Missing", out var value));
            Assert.False(value);
        }

        [Fact]
        public void Boolean_GetWithCreate_AddsEntry()
        {
            var dict = new PdfDictionary();

            var result = dict.Elements.GetBoolean("/Flag", create: true);

            Assert.False(result);
            Assert.True(dict.Elements.ContainsKey("/Flag"));
        }

        [Fact]
        public void Integer_SetAndGet_RoundTrips()
        {
            var dict = new PdfDictionary();
            dict.Elements.SetInteger("/Count", 42);

            Assert.Equal(42, dict.Elements.GetInteger("/Count"));
            Assert.True(dict.Elements.TryGetInteger("/Count", out var value));
            Assert.Equal(42, value);
        }

        [Fact]
        public void Integer_MissingKey_ReturnsZero()
        {
            var dict = new PdfDictionary();

            Assert.Equal(0, dict.Elements.GetInteger("/Missing"));
            Assert.False(dict.Elements.TryGetInteger("/Missing", out var value));
            Assert.Equal(0, value);
        }

        [Fact]
        public void Real_SetAndGet_RoundTrips()
        {
            var dict = new PdfDictionary();
            dict.Elements.SetReal("/Value", 3.5);

            Assert.Equal(3.5, dict.Elements.GetReal("/Value"));
            Assert.True(dict.Elements.TryGetReal("/Value", out var value));
            Assert.Equal(3.5, value);
        }

        [Fact]
        public void Real_ReadsIntegerAsDouble()
        {
            var dict = new PdfDictionary();
            dict.Elements.SetInteger("/Value", 7);

            Assert.Equal(7, dict.Elements.GetReal("/Value"));
        }

        [Fact]
        public void Real_MissingKey_ReturnsZero()
        {
            var dict = new PdfDictionary();

            Assert.Equal(0, dict.Elements.GetReal("/Missing"));
            Assert.False(dict.Elements.TryGetReal("/Missing", out var value));
        }

        [Fact]
        public void String_SetAndGet_RoundTrips()
        {
            var dict = new PdfDictionary();
            dict.Elements.SetString("/Title", "Hello");

            Assert.Equal("Hello", dict.Elements.GetString("/Title"));
            Assert.True(dict.Elements.TryGetString("/Title", out var value));
            Assert.Equal("Hello", value);
        }

        [Fact]
        public void String_WithEncoding_RoundTrips()
        {
            var dict = new PdfDictionary();
            dict.Elements.SetString("/Title", "Hello", PdfStringEncoding.RawEncoding);

            Assert.Equal("Hello", dict.Elements.GetString("/Title"));
        }

        [Fact]
        public void String_MissingKey_ReturnsEmpty()
        {
            var dict = new PdfDictionary();

            Assert.Equal("", dict.Elements.GetString("/Missing"));
            Assert.False(dict.Elements.TryGetString("/Missing", out var value));
        }

        [Fact]
        public void Name_SetAndGet_AddsLeadingSlash()
        {
            var dict = new PdfDictionary();
            dict.Elements.SetName("/Type", "Catalog");

            Assert.Equal("/Catalog", dict.Elements.GetName("/Type"));
        }

        [Fact]
        public void Name_SetWithExistingSlash_DoesNotDuplicateIt()
        {
            var dict = new PdfDictionary();
            dict.Elements.SetName("/Type", "/Catalog");

            Assert.Equal("/Catalog", dict.Elements.GetName("/Type"));
        }

        [Fact]
        public void Name_SetNullValue_Throws()
        {
            var dict = new PdfDictionary();

            Assert.Throws<ArgumentNullException>(() => dict.Elements.SetName("/Type", null!));
        }

        [Fact]
        public void Name_MissingKey_ReturnsEmpty()
        {
            var dict = new PdfDictionary();

            Assert.Equal("", dict.Elements.GetName("/Missing"));
        }

        [Fact]
        public void Rectangle_SetAndGet_RoundTrips()
        {
            var dict = new PdfDictionary();
            var rect = new PdfRectangle(1, 2, 3, 4);
            dict.Elements.SetRectangle("/Box", rect);

            var result = dict.Elements.GetRectangle("/Box");

            Assert.Equal(rect.X1, result.X1);
            Assert.Equal(rect.Y1, result.Y1);
        }

        [Fact]
        public void Rectangle_MissingKey_ReturnsEmptyRectangle()
        {
            var dict = new PdfDictionary();

            var result = dict.Elements.GetRectangle("/Missing");

            Assert.False(dict.Elements.TryGetRectangle("/Missing", out _));
            Assert.Equal(0, result.X1);
        }

        [Fact]
        public void DateTime_MissingKey_ReturnsDefaultValue()
        {
            var dict = new PdfDictionary();
            var defaultValue = new DateTime(2020, 1, 1);

            var result = dict.Elements.GetDateTime("/Created", defaultValue);

            Assert.Equal(defaultValue, result);
        }

        [Fact]
        public void DateTime_SetAndGet_RoundTrips()
        {
            var dict = new PdfDictionary();
            var value = new DateTime(2024, 6, 15, 10, 30, 0);
            dict.Elements.SetDateTime("/Created", value);

            var result = dict.Elements.GetDateTime("/Created", DateTime.MinValue);

            Assert.Equal(value, result);
        }

        [Fact]
        public void Value_SetAndGet_RoundTrips()
        {
            var dict = new PdfDictionary();
            var item = new PdfInteger(5);
            dict.Elements.SetValue("/Key", item);

            Assert.Same(item, dict.Elements.GetValue("/Key"));
        }

        [Fact]
        public void Value_MissingKey_ReturnsNull()
        {
            var dict = new PdfDictionary();

            Assert.Null(dict.Elements.GetValue("/Missing"));
        }

        [Fact]
        public void Indexer_SetNull_Throws()
        {
            var dict = new PdfDictionary();

            Assert.Throws<ArgumentNullException>(() => dict.Elements["/Key"] = null!);
        }

        [Fact]
        public void Indexer_ByPdfName_SetAndGet_RoundTrips()
        {
            var dict = new PdfDictionary();
            var name = new PdfName("/Key");
            var item = new PdfInteger(9);

            dict.Elements[name] = item;

            Assert.Same(item, dict.Elements[name]);
            Assert.Same(item, dict.Elements["/Key"]);
        }

        [Fact]
        public void Add_KeyWithoutSlash_Throws()
        {
            var dict = new PdfDictionary();

            Assert.Throws<ArgumentException>(() => dict.Elements.Add("Key", new PdfInteger(1)));
        }

        [Fact]
        public void Add_EmptyKey_Throws()
        {
            var dict = new PdfDictionary();

            Assert.Throws<ArgumentNullException>(() => dict.Elements.Add("", new PdfInteger(1)));
        }

        [Fact]
        public void Add_ValidKey_AddsEntry()
        {
            var dict = new PdfDictionary();
            dict.Elements.Add("/Key", new PdfInteger(1));

            Assert.True(dict.Elements.ContainsKey("/Key"));
            Assert.Equal(1, dict.Elements.Count);
        }

        [Fact]
        public void Add_KeyValuePairOverload_AddsEntry()
        {
            var dict = new PdfDictionary();
            dict.Elements.Add(new KeyValuePair<string, PdfItem>("/Key", new PdfInteger(1)));

            Assert.True(dict.Elements.ContainsKey("/Key"));
        }

        [Fact]
        public void Remove_ExistingKey_ReturnsTrueAndRemoves()
        {
            var dict = new PdfDictionary();
            dict.Elements.SetInteger("/Key", 1);

            Assert.True(dict.Elements.Remove("/Key"));
            Assert.False(dict.Elements.ContainsKey("/Key"));
        }

        [Fact]
        public void Remove_MissingKey_ReturnsFalse()
        {
            var dict = new PdfDictionary();

            Assert.False(dict.Elements.Remove("/Missing"));
        }

        [Fact]
        public void Remove_KeyValuePairOverload_NotImplemented()
        {
            var dict = new PdfDictionary();

            Assert.Throws<NotImplementedException>(() =>
                dict.Elements.Remove(new KeyValuePair<string, PdfItem>("/Key", new PdfInteger(1))));
        }

        [Fact]
        public void Contains_KeyValuePairOverload_NotImplemented()
        {
            var dict = new PdfDictionary();

            Assert.Throws<NotImplementedException>(() =>
                dict.Elements.Contains(new KeyValuePair<string, PdfItem>("/Key", new PdfInteger(1))));
        }

        [Fact]
        public void Clear_RemovesAllEntries()
        {
            var dict = new PdfDictionary();
            dict.Elements.SetInteger("/A", 1);
            dict.Elements.SetInteger("/B", 2);

            dict.Elements.Clear();

            Assert.Equal(0, dict.Elements.Count);
        }

        [Fact]
        public void Count_ReflectsNumberOfEntries()
        {
            var dict = new PdfDictionary();
            Assert.Equal(0, dict.Elements.Count);

            dict.Elements.SetInteger("/A", 1);
            Assert.Equal(1, dict.Elements.Count);
        }

        [Fact]
        public void TryGetValue_ExistingAndMissingKeys()
        {
            var dict = new PdfDictionary();
            dict.Elements.SetInteger("/Key", 5);

            Assert.True(dict.Elements.TryGetValue("/Key", out var found));
            Assert.NotNull(found);
            Assert.False(dict.Elements.TryGetValue("/Missing", out var missing));
            Assert.Null(missing);
        }

        [Fact]
        public void IsReadOnly_IsAlwaysFalse()
        {
            var dict = new PdfDictionary();

            Assert.False(dict.Elements.IsReadOnly);
        }

        [Fact]
        public void IsFixedSize_IsAlwaysFalse()
        {
            var dict = new PdfDictionary();

            Assert.False(dict.Elements.IsFixedSize);
        }

        [Fact]
        public void CopyTo_NotImplemented()
        {
            var dict = new PdfDictionary();
            dict.Elements.SetInteger("/A", 1);

            var array = new KeyValuePair<string, PdfItem>[1];

            Assert.Throws<NotImplementedException>(() => dict.Elements.CopyTo(array, 0));
        }

        [Fact]
        public void Keys_ReturnsAllKeyNames()
        {
            var dict = new PdfDictionary();
            dict.Elements.SetInteger("/A", 1);
            dict.Elements.SetInteger("/B", 2);

            Assert.Equal(2, dict.Elements.Keys.Count);
            Assert.Contains("/A", dict.Elements.Keys);
            Assert.Contains("/B", dict.Elements.Keys);
        }

        [Fact]
        public void KeyNames_ReturnsPdfNameArray()
        {
            var dict = new PdfDictionary();
            dict.Elements.SetInteger("/A", 1);

            var keyNames = dict.Elements.KeyNames;

            Assert.Single(keyNames);
            Assert.Equal("/A", keyNames[0].Value);
        }

        [Fact]
        public void GetEnumerator_EnumeratesAllEntries()
        {
            var dict = new PdfDictionary();
            dict.Elements.SetInteger("/A", 1);
            dict.Elements.SetInteger("/B", 2);

            var keys = new List<string>();
            foreach (var kv in dict.Elements)
                keys.Add(kv.Key);

            Assert.Contains("/A", keys);
            Assert.Contains("/B", keys);
        }

        [Fact]
        public void Clone_ProducesIndependentCopy()
        {
            var dict = new PdfDictionary();
            dict.Elements.SetInteger("/A", 1);

            var clone = dict.Elements.Clone();
            clone.SetInteger("/A", 2);

            Assert.Equal(1, dict.Elements.GetInteger("/A"));
            Assert.Equal(2, clone.GetInteger("/A"));
        }

        [Fact]
        public void Rectangle_TryGet_ExistingDirectRectangle_ReturnsTrue()
        {
            var dict = new PdfDictionary();
            var rect = new PdfRectangle(1, 2, 3, 4);
            dict.Elements.SetRectangle("/Box", rect);

            Assert.True(dict.Elements.TryGetRectangle("/Box", out var value));
            Assert.Equal(rect.X1, value.X1);
        }

        [Fact]
        public void GetMatrix_MissingKey_ReturnsIdentity()
        {
            var dict = new PdfDictionary();

            var matrix = dict.Elements.GetMatrix("/Matrix");

            Assert.Equal(new PeachPDF.PdfSharpCore.Drawing.XMatrix(), matrix);
        }

        [Fact]
        public void GetMatrix_ArrayWithSixElements_ParsesValues()
        {
            var dict = new PdfDictionary();
            var array = new PdfArray();
            array.Elements.Add(new PdfReal(1));
            array.Elements.Add(new PdfReal(0));
            array.Elements.Add(new PdfReal(0));
            array.Elements.Add(new PdfReal(1));
            array.Elements.Add(new PdfReal(5));
            array.Elements.Add(new PdfReal(6));
            dict.Elements["/Matrix"] = array;

            var matrix = dict.Elements.GetMatrix("/Matrix");

            Assert.Equal(5, matrix.OffsetX);
            Assert.Equal(6, matrix.OffsetY);
        }

        [Fact]
        public void GetMatrix_NonArrayValue_ThrowsInvalidCastException()
        {
            var dict = new PdfDictionary();
            dict.Elements.SetInteger("/Matrix", 42);

            Assert.Throws<InvalidCastException>(() => dict.Elements.GetMatrix("/Matrix"));
        }

        [Fact]
        public void SetMatrix_ThenGetMatrix_ThrowsNotImplementedException()
        {
            // SetMatrix stores the value as a PdfLiteral (e.g. "[1 0 0 1 0 0]"), but GetMatrix's
            // PdfLiteral branch is an unimplemented stub that always throws -- so a value written
            // via SetMatrix can never be read back via GetMatrix on this fork. Real, pre-existing
            // bug found via this test; documented rather than fixed here.
            var dict = new PdfDictionary();

            dict.Elements.SetMatrix("/Matrix", new PeachPDF.PdfSharpCore.Drawing.XMatrix());

            Assert.Throws<NotImplementedException>(() => dict.Elements.GetMatrix("/Matrix"));
        }

        [Fact]
        public void GetObject_MissingKey_ReturnsNull()
        {
            var dict = new PdfDictionary();

            Assert.Null(dict.Elements.GetObject("/Missing"));
            Assert.Null(dict.Elements.GetDictionary("/Missing"));
            Assert.Null(dict.Elements.GetArray("/Missing"));
            Assert.Null(dict.Elements.GetReference("/Missing"));
        }

        [Fact]
        public void GetObject_DirectValue_ReturnsSameObject()
        {
            var dict = new PdfDictionary();
            var inner = new PdfDictionary();
            dict.Elements.SetObject("/Inner", inner);

            Assert.Same(inner, dict.Elements.GetObject("/Inner"));
            Assert.Same(inner, dict.Elements.GetDictionary("/Inner"));
        }

        [Fact]
        public void GetArray_DirectValue_ReturnsSameArray()
        {
            var dict = new PdfDictionary();
            var array = new PdfArray();
            dict.Elements["/Items"] = array;

            Assert.Same(array, dict.Elements.GetArray("/Items"));
        }

        [Fact]
        public void SetObject_IndirectObject_Throws()
        {
            var doc = new PdfDocument();
            var inner = new PdfDictionary();
            doc.Internals.AddObject(inner);
            var dict = new PdfDictionary();

            Assert.Throws<ArgumentException>(() => dict.Elements.SetObject("/Inner", inner));
        }

        [Fact]
        public void GetObject_IndirectReference_ReturnsReferencedObject()
        {
            var doc = new PdfDocument();
            var inner = new PdfDictionary();
            doc.Internals.AddObject(inner);
            var dict = new PdfDictionary();
            dict.Elements.SetReference("/Inner", inner);

            Assert.Same(inner, dict.Elements.GetObject("/Inner"));
            Assert.Same(inner.Reference, dict.Elements.GetReference("/Inner"));
        }

        [Fact]
        public void SetReference_DirectObject_Throws()
        {
            var dict = new PdfDictionary();
            var inner = new PdfDictionary();

            Assert.Throws<ArgumentException>(() => dict.Elements.SetReference("/Inner", inner));
        }

        [Fact]
        public void SetReference_ByPdfReference_RoundTrips()
        {
            var doc = new PdfDocument();
            var inner = new PdfDictionary();
            doc.Internals.AddObject(inner);
            var dict = new PdfDictionary();

            dict.Elements.SetReference("/Inner", inner.Reference);

            Assert.Same(inner.Reference, dict.Elements.GetReference("/Inner"));
        }

        [Fact]
        public void SetReference_NullReference_Throws()
        {
            var dict = new PdfDictionary();

            Assert.Throws<ArgumentNullException>(() => dict.Elements.SetReference("/Inner", (PdfReference)null!));
        }

        [Fact]
        public void EnumFromName_AfterSetEnumAsName_RoundTrips()
        {
            var dict = new PdfDictionary();
            dict.Elements.SetEnumAsName("/Layout", PdfPageLayout.TwoColumnLeft);

            var result = (PdfPageLayout)dict.Elements.GetEnumFromName("/Layout", PdfPageLayout.SinglePage);

            Assert.Equal(PdfPageLayout.TwoColumnLeft, result);
        }

        [Fact]
        public void EnumFromName_MissingKey_ReturnsDefault()
        {
            var dict = new PdfDictionary();

            var result = (PdfPageMode)dict.Elements.GetEnumFromName("/Mode", PdfPageMode.FullScreen);

            Assert.Equal(PdfPageMode.FullScreen, result);
        }

        [Fact]
        public void EnumFromName_MissingKeyWithCreate_ThrowsBecauseDefaultLacksLeadingSlash()
        {
            // The create branch stores `new PdfName(defaultValue.ToString())`, but an enum's
            // ToString() never includes a leading slash while PdfName's constructor requires one --
            // so GetEnumFromName(..., create: true) always throws for any enum default. Real,
            // pre-existing bug; documented here rather than fixed.
            var dict = new PdfDictionary();

            Assert.Throws<ArgumentException>(() => dict.Elements.GetEnumFromName("/Mode", PdfPageMode.UseOutlines, create: true));
        }

        [Fact]
        public void SetEnumAsName_NonEnumValue_Throws()
        {
            var dict = new PdfDictionary();

            Assert.Throws<ArgumentException>(() => dict.Elements.SetEnumAsName("/Mode", "not-an-enum"));
        }
    }
}
