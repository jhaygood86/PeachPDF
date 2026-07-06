using PeachPDF.PdfSharpCore.Pdf;
using System;

namespace PeachPDF.Tests.PdfSharpCoreTests.Pdf
{
    public class PdfArrayTests
    {
        [Fact]
        public void Constructor_WithDocumentAndItems_AddsAllItems()
        {
            var document = new PdfDocument();
            var array = new PdfArray(document, new PdfInteger(1), new PdfInteger(2));

            Assert.Equal(2, array.Elements.Count);
        }

        [Fact]
        public void Elements_Add_AppendsItem()
        {
            var array = new PdfArray();
            array.Elements.Add(new PdfInteger(1));

            Assert.Equal(1, array.Elements.Count);
            Assert.Equal(1, array.Elements.GetInteger(0));
        }

        [Fact]
        public void Elements_Indexer_SetNull_Throws()
        {
            var array = new PdfArray();
            array.Elements.Add(new PdfInteger(1));

            Assert.Throws<ArgumentNullException>(() => array.Elements[0] = null!);
        }

        [Fact]
        public void GetBoolean_ReturnsStoredValue()
        {
            var array = new PdfArray();
            array.Elements.Add(new PdfBoolean(true));

            Assert.True(array.Elements.GetBoolean(0));
        }

        [Fact]
        public void GetBoolean_IndexOutOfRange_Throws()
        {
            var array = new PdfArray();

            Assert.Throws<ArgumentOutOfRangeException>(() => array.Elements.GetBoolean(0));
        }

        [Fact]
        public void GetInteger_ReturnsStoredValue()
        {
            var array = new PdfArray();
            array.Elements.Add(new PdfInteger(42));

            Assert.Equal(42, array.Elements.GetInteger(0));
        }

        [Fact]
        public void GetInteger_IndexOutOfRange_Throws()
        {
            var array = new PdfArray();

            Assert.Throws<ArgumentOutOfRangeException>(() => array.Elements.GetInteger(0));
        }

        [Fact]
        public void GetReal_ReturnsStoredValue()
        {
            var array = new PdfArray();
            array.Elements.Add(new PdfReal(3.5));

            Assert.Equal(3.5, array.Elements.GetReal(0));
        }

        [Fact]
        public void GetReal_ReadsIntegerAsDouble()
        {
            var array = new PdfArray();
            array.Elements.Add(new PdfInteger(7));

            Assert.Equal(7, array.Elements.GetReal(0));
        }

        [Fact]
        public void GetString_ReturnsStoredValue()
        {
            var array = new PdfArray();
            array.Elements.Add(new PdfString("Hello"));

            Assert.Equal("Hello", array.Elements.GetString(0));
        }

        [Fact]
        public void GetName_ReturnsStoredValue()
        {
            var array = new PdfArray();
            array.Elements.Add(new PdfName("/Foo"));

            Assert.Equal("/Foo", array.Elements.GetName(0));
        }

        [Fact]
        public void GetObject_ReturnsPdfObjectDirectly()
        {
            var array = new PdfArray();
            var dict = new PdfDictionary();
            array.Elements.Add(dict);

            Assert.Same(dict, array.Elements.GetObject(0));
        }

        [Fact]
        public void GetDictionary_ReturnsStoredDictionary()
        {
            var array = new PdfArray();
            var dict = new PdfDictionary();
            array.Elements.Add(dict);

            Assert.Same(dict, array.Elements.GetDictionary(0));
        }

        [Fact]
        public void GetDictionary_WrongType_ReturnsNull()
        {
            var array = new PdfArray();
            array.Elements.Add(new PdfInteger(1));

            Assert.Null(array.Elements.GetDictionary(0));
        }

        [Fact]
        public void GetArray_ReturnsStoredArray()
        {
            var array = new PdfArray();
            var inner = new PdfArray();
            array.Elements.Add(inner);

            Assert.Same(inner, array.Elements.GetArray(0));
        }

        [Fact]
        public void GetReference_NonReferenceItem_ReturnsNull()
        {
            var array = new PdfArray();
            array.Elements.Add(new PdfInteger(1));

            Assert.Null(array.Elements.GetReference(0));
        }

        [Fact]
        public void Items_ReturnsAllElementsAsArray()
        {
            var array = new PdfArray();
            array.Elements.Add(new PdfInteger(1));
            array.Elements.Add(new PdfInteger(2));

            var items = array.Elements.Items;

            Assert.Equal(2, items.Length);
        }

        [Fact]
        public void RemoveAt_RemovesElementAtIndex()
        {
            var array = new PdfArray();
            array.Elements.Add(new PdfInteger(1));
            array.Elements.Add(new PdfInteger(2));

            array.Elements.RemoveAt(0);

            Assert.Equal(1, array.Elements.Count);
            Assert.Equal(2, array.Elements.GetInteger(0));
        }

        [Fact]
        public void Remove_RemovesFirstMatchingItem()
        {
            var array = new PdfArray();
            var item = new PdfInteger(1);
            array.Elements.Add(item);

            Assert.True(array.Elements.Remove(item));
            Assert.Equal(0, array.Elements.Count);
        }

        [Fact]
        public void Remove_MissingItem_ReturnsFalse()
        {
            var array = new PdfArray();

            Assert.False(array.Elements.Remove(new PdfInteger(1)));
        }

        [Fact]
        public void Insert_PlacesItemAtIndex()
        {
            var array = new PdfArray();
            array.Elements.Add(new PdfInteger(1));
            array.Elements.Insert(0, new PdfInteger(0));

            Assert.Equal(0, array.Elements.GetInteger(0));
            Assert.Equal(1, array.Elements.GetInteger(1));
        }

        [Fact]
        public void Contains_FindsExistingItem()
        {
            var array = new PdfArray();
            var item = new PdfInteger(1);
            array.Elements.Add(item);

            Assert.True(array.Elements.Contains(item));
        }

        [Fact]
        public void Clear_RemovesAllItems()
        {
            var array = new PdfArray();
            array.Elements.Add(new PdfInteger(1));
            array.Elements.Add(new PdfInteger(2));

            array.Elements.Clear();

            Assert.Equal(0, array.Elements.Count);
        }

        [Fact]
        public void IndexOf_ReturnsPositionOfItem()
        {
            var array = new PdfArray();
            var item = new PdfInteger(1);
            array.Elements.Add(new PdfInteger(0));
            array.Elements.Add(item);

            Assert.Equal(1, array.Elements.IndexOf(item));
        }

        [Fact]
        public void IsReadOnly_And_IsFixedSize_AreFalse()
        {
            var array = new PdfArray();

            Assert.False(array.Elements.IsReadOnly);
            Assert.False(array.Elements.IsFixedSize);
            Assert.False(array.Elements.IsSynchronized);
        }

        [Fact]
        public void SyncRoot_ReturnsNull()
        {
            var array = new PdfArray();

            Assert.Null(array.Elements.SyncRoot);
        }

        [Fact]
        public void CopyTo_CopiesElementsIntoTargetArray()
        {
            var array = new PdfArray();
            array.Elements.Add(new PdfInteger(1));
            array.Elements.Add(new PdfInteger(2));

            var target = new PdfItem[2];
            array.Elements.CopyTo(target, 0);

            Assert.Equal(2, ((PdfInteger)target[1]).Value);
        }

        [Fact]
        public void GetEnumerator_EnumeratesAllItems()
        {
            var array = new PdfArray();
            array.Elements.Add(new PdfInteger(1));
            array.Elements.Add(new PdfInteger(2));

            var sum = 0;
            foreach (var item in array.Elements)
                sum += ((PdfInteger)item).Value;

            Assert.Equal(3, sum);
        }

        [Fact]
        public void Clone_ProducesIndependentCopy()
        {
            var array = new PdfArray();
            array.Elements.Add(new PdfInteger(1));

            var clone = array.Elements.Clone();
            clone.Add(new PdfInteger(2));

            Assert.Equal(1, array.Elements.Count);
            Assert.Equal(2, clone.Count);
        }

        [Fact]
        public void ToString_ProducesBracketedRepresentation()
        {
            var array = new PdfArray();
            array.Elements.Add(new PdfInteger(1));
            array.Elements.Add(new PdfInteger(2));

            var text = array.ToString();

            Assert.StartsWith("[", text);
            Assert.EndsWith("]", text.TrimEnd());
        }

        [Fact]
        public void GetEnumerator_OnArray_EnumeratesElements()
        {
            var array = new PdfArray();
            array.Elements.Add(new PdfInteger(1));
            array.Elements.Add(new PdfInteger(2));

            var count = 0;
            foreach (var item in array)
                count++;

            Assert.Equal(2, count);
        }
    }
}
