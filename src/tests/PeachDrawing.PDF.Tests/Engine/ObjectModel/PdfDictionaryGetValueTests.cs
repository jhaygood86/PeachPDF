using PeachDrawing.PDF.Engine;
using PeachDrawing.PDF.Engine.Advanced;
using System;
using Xunit;

namespace PeachPDF.Tests.PdfEngineTests.ObjectModel
{
    /// <summary>
    /// Exercises <see cref="PdfDictionary.DictionaryElements.GetOrCreate{TItem}"/>, <c>GetExisting</c> and their array counterparts
    /// with test-only dictionary and array classes: creating a missing value (direct or indirect), following a reference, returning a
    /// value that already has the right class and wrapping one that has a more general class.
    /// </summary>
    public class PdfDictionaryGetValueTests
    {
        private sealed class TestSubDictionary : PdfDictionary, IPdfDictionaryValue<TestSubDictionary>
        {
            public TestSubDictionary() { }
            public TestSubDictionary(PdfDocument document) : base(document) { }
            public TestSubDictionary(PdfDictionary dict) : base(dict) { }

            static TestSubDictionary IPdfDictionaryValue<TestSubDictionary>.Create(PdfDocument document) => new(document);
            static TestSubDictionary IPdfDictionaryValue<TestSubDictionary>.Wrap(PdfDictionary existing) => new(existing);
        }

        private sealed class TestSubArray : PdfArray, IPdfArrayValue<TestSubArray>
        {
            public TestSubArray() { }
            public TestSubArray(PdfDocument document) : base(document) { }
            public TestSubArray(PdfArray array) : base(array) { }

            static TestSubArray IPdfArrayValue<TestSubArray>.Create(PdfDocument document) => new(document);
            static TestSubArray IPdfArrayValue<TestSubArray>.Wrap(PdfArray existing) => new(existing);
        }

        private const string SubDict = "/SubDict";
        private const string SubArray = "/SubArray";

        private static (PdfDocument Document, PdfDictionary Dictionary) CreateOwned()
        {
            var document = new PdfDocument();
            var dict = new PdfDictionary(document);
            document.Internals.AddObject(dict);
            return (document, dict);
        }

        [Fact]
        public void GetExisting_MissingKey_ReturnsNull()
        {
            var (_, dict) = CreateOwned();

            Assert.Null(dict.Elements.GetExisting<TestSubDictionary>(SubDict));
            Assert.Null(dict.Elements.GetExistingArray<TestSubArray>(SubArray));
        }

        [Fact]
        public void GetOrCreate_MissingDictionaryKeyCreateDirect_CreatesDirectValue()
        {
            var (_, dict) = CreateOwned();

            var sub = dict.Elements.GetOrCreate<TestSubDictionary>(SubDict, ValueCreationFlags.Create);

            Assert.False(sub.IsIndirect);
            Assert.Same(sub, dict.Elements[SubDict]);
        }

        [Fact]
        public void GetOrCreate_MissingDictionaryKeyCreateIndirect_CreatesIndirectValue()
        {
            var (_, dict) = CreateOwned();

            var sub = dict.Elements.GetOrCreate<TestSubDictionary>(SubDict, ValueCreationFlags.CreateIndirect);

            Assert.True(sub.IsIndirect);
            Assert.IsType<PdfReference>(dict.Elements[SubDict]);
        }

        [Fact]
        public void GetOrCreateArray_MissingKeyCreateDirect_CreatesDirectValue()
        {
            var (_, dict) = CreateOwned();

            var sub = dict.Elements.GetOrCreateArray<TestSubArray>(SubArray, ValueCreationFlags.Create);

            Assert.Same(sub, dict.Elements[SubArray]);
        }

        [Fact]
        public void GetOrCreateArray_MissingKeyCreateIndirect_CreatesIndirectValue()
        {
            var (_, dict) = CreateOwned();

            var sub = dict.Elements.GetOrCreateArray<TestSubArray>(SubArray, ValueCreationFlags.CreateIndirect);

            Assert.True(sub.IsIndirect);
            Assert.IsType<PdfReference>(dict.Elements[SubArray]);
        }

        [Fact]
        public void GetOrCreate_CalledTwice_ReturnsTheValueTheFirstCallCreated()
        {
            var (_, dict) = CreateOwned();

            var first = dict.Elements.GetOrCreate<TestSubDictionary>(SubDict, ValueCreationFlags.Create);
            var second = dict.Elements.GetOrCreate<TestSubDictionary>(SubDict, ValueCreationFlags.Create);

            Assert.Same(first, second);
        }

        [Fact]
        public void GetOrCreate_IndirectValueAlreadyCorrectType_ReturnsItUnchanged()
        {
            var (document, dict) = CreateOwned();
            var sub = new TestSubDictionary(document);
            document.Internals.AddObject(sub);
            dict.Elements.SetReference(SubDict, sub);

            Assert.Same(sub, dict.Elements.GetOrCreate<TestSubDictionary>(SubDict, ValueCreationFlags.Create));
        }

        [Fact]
        public void GetOrCreate_IndirectDictionaryValueOfGeneralType_WrapsItInTheRequestedType()
        {
            var (document, dict) = CreateOwned();
            var plain = new PdfDictionary(document);
            plain.Elements.SetInteger("/Marker", 7);
            document.Internals.AddObject(plain);
            dict.Elements.SetReference(SubDict, plain);

            var value = dict.Elements.GetOrCreate<TestSubDictionary>(SubDict, ValueCreationFlags.Create);

            Assert.Equal(7, value.Elements.GetInteger("/Marker"));
        }

        [Fact]
        public void GetOrCreateArray_IndirectValueOfGeneralType_WrapsItInTheRequestedType()
        {
            var (document, dict) = CreateOwned();
            var plain = new PdfArray(document);
            document.Internals.AddObject(plain);
            dict.Elements.SetReference(SubArray, plain);

            var value = dict.Elements.GetOrCreateArray<TestSubArray>(SubArray, ValueCreationFlags.Create);

            Assert.IsType<TestSubArray>(value);
        }

        [Fact]
        public void GetExisting_DirectDictionaryValueOfGeneralType_WrapsItInTheRequestedType()
        {
            var (_, dict) = CreateOwned();
            dict.Elements[SubDict] = new PdfDictionary();

            Assert.IsType<TestSubDictionary>(dict.Elements.GetExisting<TestSubDictionary>(SubDict));
        }

        [Fact]
        public void GetExisting_DirectDictionaryValueAlreadyCorrectType_ReturnsItUnchanged()
        {
            var (_, dict) = CreateOwned();
            var sub = new TestSubDictionary();
            dict.Elements[SubDict] = sub;

            Assert.Same(sub, dict.Elements.GetExisting<TestSubDictionary>(SubDict));
        }

        [Fact]
        public void GetExistingArray_DirectArrayValueOfGeneralType_WrapsItInTheRequestedType()
        {
            var (_, dict) = CreateOwned();
            dict.Elements[SubArray] = new PdfArray();

            Assert.IsType<TestSubArray>(dict.Elements.GetExistingArray<TestSubArray>(SubArray));
        }

        [Fact]
        public void GetExisting_DirectDictionaryValueAsGeneralRequest_ReturnsAnySubclassUnchanged()
        {
            var (_, dict) = CreateOwned();
            var sub = new TestSubDictionary();
            dict.Elements[SubDict] = sub;

            Assert.Same(sub, dict.Elements.GetExisting<PdfDictionary>(SubDict));
        }

        [Fact]
        public void GetExisting_ValueThatIsNotADictionary_Throws()
        {
            var (_, dict) = CreateOwned();
            dict.Elements.SetInteger(SubDict, 3);

            Assert.Throws<InvalidCastException>(() => dict.Elements.GetExisting<TestSubDictionary>(SubDict));
        }

        [Fact]
        public void GetExistingArray_ValueThatIsNotAnArray_Throws()
        {
            var (_, dict) = CreateOwned();
            dict.Elements.SetInteger(SubArray, 3);

            Assert.Throws<InvalidCastException>(() => dict.Elements.GetExistingArray<TestSubArray>(SubArray));
        }
    }
}
