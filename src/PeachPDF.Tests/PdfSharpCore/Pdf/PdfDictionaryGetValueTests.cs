using PeachPDF.PdfSharpCore.Pdf;
using PeachPDF.PdfSharpCore.Pdf.Advanced;
using System;
using Xunit;

namespace PeachPDF.Tests.PdfSharpCoreTests.Pdf
{
    /// <summary>
    /// Exercises <see cref="PdfDictionary.DictionaryElements.GetValue(string, VCF)"/>'s private helpers
    /// (CreateMissingValue/ResolveIndirectValue/CoerceDictionary/CoerceArray) directly, using a
    /// test-only dictionary type whose <c>Keys</c> declare object types under our control - the real
    /// PDF dictionary types don't expose a scenario for every branch (e.g. a key declared as one
    /// concrete dictionary/array subtype whose stored value is a different, incompatible one).
    /// </summary>
    public class PdfDictionaryGetValueTests
    {
        private sealed class TestSubDictionary : PdfDictionary
        {
            public TestSubDictionary() { }
            public TestSubDictionary(PdfDocument document) : base(document) { }
            public TestSubDictionary(PdfDictionary dict) : base(dict) { }
        }

        private sealed class TestSubArray : PdfArray
        {
            public TestSubArray() { }
            public TestSubArray(PdfDocument document) : base(document) { }
            public TestSubArray(PdfArray array) : base(array) { }
        }

        private sealed class TestKeyedDictionary : PdfDictionary
        {
            public TestKeyedDictionary() { }
            public TestKeyedDictionary(PdfDocument document) : base(document) { }

            internal override DictionaryMeta Meta => Keys.Meta;

            internal sealed class Keys : KeysBase
            {
                [KeyInfo(KeyType.Dictionary | KeyType.Optional, typeof(TestSubDictionary))]
                public const string SubDict = "/SubDict";

                [KeyInfo(KeyType.Array | KeyType.Optional, typeof(TestSubArray))]
                public const string SubArray = "/SubArray";

                [KeyInfo(KeyType.Integer | KeyType.Optional, typeof(PdfInteger))]
                public const string BadType = "/BadType";

                public static DictionaryMeta Meta => _meta ??= CreateMeta(typeof(Keys));
                static DictionaryMeta? _meta;
            }
        }

        private static (PdfDocument Document, TestKeyedDictionary Dictionary) CreateOwned()
        {
            var document = new PdfDocument();
            var dict = new TestKeyedDictionary(document);
            document.Internals.AddObject(dict);
            return (document, dict);
        }

        [Fact]
        public void GetValue_MissingKeyWithNoneOptions_ReturnsNull()
        {
            var (_, dict) = CreateOwned();

            Assert.Null(dict.Elements.GetValue(TestKeyedDictionary.Keys.SubDict, VCF.None));
        }

        [Fact]
        public void GetValue_MissingDictionaryKeyCreateDirect_CreatesDirectValue()
        {
            var (_, dict) = CreateOwned();

            var value = dict.Elements.GetValue(TestKeyedDictionary.Keys.SubDict, VCF.Create);

            var sub = Assert.IsType<TestSubDictionary>(value);
            Assert.Same(sub, dict.Elements[TestKeyedDictionary.Keys.SubDict]);
        }

        [Fact]
        public void GetValue_MissingDictionaryKeyCreateIndirect_CreatesIndirectValue()
        {
            var (_, dict) = CreateOwned();

            var value = dict.Elements.GetValue(TestKeyedDictionary.Keys.SubDict, VCF.CreateIndirect);

            var sub = Assert.IsType<TestSubDictionary>(value);
            Assert.True(sub.IsIndirect);
            Assert.IsType<PdfReference>(dict.Elements[TestKeyedDictionary.Keys.SubDict]);
        }

        [Fact]
        public void GetValue_MissingArrayKeyCreateDirect_CreatesDirectValue()
        {
            var (_, dict) = CreateOwned();

            var value = dict.Elements.GetValue(TestKeyedDictionary.Keys.SubArray, VCF.Create);

            Assert.IsType<TestSubArray>(value);
        }

        [Fact]
        public void GetValue_MissingKeyWithUndeclaredType_ThrowsNotImplemented()
        {
            var (_, dict) = CreateOwned();

            var ex = Assert.Throws<NotImplementedException>(
                () => dict.Elements.GetValue("/Undeclared", VCF.Create));
            Assert.Contains("Cannot create value for key", ex.Message);
        }

        [Fact]
        public void GetValue_MissingKeyWithNonDictionaryOrArrayType_ThrowsNotImplemented()
        {
            var (_, dict) = CreateOwned();

            var ex = Assert.Throws<NotImplementedException>(
                () => dict.Elements.GetValue(TestKeyedDictionary.Keys.BadType, VCF.Create));
            Assert.Contains("Type other than array or dictionary", ex.Message);
        }

        [Fact]
        public void GetValue_IndirectValueAlreadyCorrectType_ReturnsItUnchanged()
        {
            var (document, dict) = CreateOwned();
            var sub = new TestSubDictionary(document);
            document.Internals.AddObject(sub);
            dict.Elements.SetReference(TestKeyedDictionary.Keys.SubDict, sub);

            var value = dict.Elements.GetValue(TestKeyedDictionary.Keys.SubDict);

            Assert.Same(sub, value);
        }

        [Fact]
        public void GetValue_IndirectDictionaryValueWrongType_CoercesToDeclaredType()
        {
            var (document, dict) = CreateOwned();
            var plain = new PdfDictionary(document);
            document.Internals.AddObject(plain);
            dict.Elements.SetReference(TestKeyedDictionary.Keys.SubDict, plain);

            var value = dict.Elements.GetValue(TestKeyedDictionary.Keys.SubDict);

            Assert.IsType<TestSubDictionary>(value);
        }

        [Fact]
        public void GetValue_IndirectArrayValueWrongType_CoercesToDeclaredType()
        {
            var (document, dict) = CreateOwned();
            var plain = new PdfArray(document);
            document.Internals.AddObject(plain);
            dict.Elements.SetReference(TestKeyedDictionary.Keys.SubArray, plain);

            var value = dict.Elements.GetValue(TestKeyedDictionary.Keys.SubArray);

            Assert.IsType<TestSubArray>(value);
        }

        [Fact]
        public void GetValue_DirectDictionaryValueWrongType_CoercesToDeclaredType()
        {
            var (_, dict) = CreateOwned();
            dict.Elements[TestKeyedDictionary.Keys.SubDict] = new PdfDictionary();

            var value = dict.Elements.GetValue(TestKeyedDictionary.Keys.SubDict);

            Assert.IsType<TestSubDictionary>(value);
        }

        [Fact]
        public void GetValue_DirectDictionaryValueAlreadyCorrectType_ReturnsItUnchanged()
        {
            var (_, dict) = CreateOwned();
            var sub = new TestSubDictionary();
            dict.Elements[TestKeyedDictionary.Keys.SubDict] = sub;

            var value = dict.Elements.GetValue(TestKeyedDictionary.Keys.SubDict);

            Assert.Same(sub, value);
        }

        [Fact]
        public void GetValue_DirectArrayValueWrongType_CoercesToDeclaredType()
        {
            var (_, dict) = CreateOwned();
            dict.Elements[TestKeyedDictionary.Keys.SubArray] = new PdfArray();

            var value = dict.Elements.GetValue(TestKeyedDictionary.Keys.SubArray);

            Assert.IsType<TestSubArray>(value);
        }

        [Fact]
        public void GetValue_DirectArrayValueForUndeclaredKey_ReturnsItUnchanged()
        {
            // CoerceArray's "type is null" branch: an array value stored under a key this dictionary
            // type never declared has no meta entry, so GetValueType returns null and the value is
            // returned exactly as stored.
            var (_, dict) = CreateOwned();
            var array = new PdfArray();
            dict.Elements["/Undeclared"] = array;

            var value = dict.Elements.GetValue("/Undeclared");

            Assert.Same(array, value);
        }
    }
}
