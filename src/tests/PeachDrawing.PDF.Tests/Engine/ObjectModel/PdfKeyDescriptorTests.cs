using PeachDrawing.PDF.Engine;
using System;
using Xunit;

namespace PeachPDF.Tests.PdfEngineTests.ObjectModel
{
    public class KeyDescriptorTests
    {
        private static PdfKeyDescriptor CreateDescriptor(PdfKeyType keyType) =>
            new PdfKeyDescriptor(new PdfKeyAttribute(keyType));

        [Theory]
        [InlineData((int)PdfKeyType.Name, typeof(PdfName))]
        [InlineData((int)PdfKeyType.String, typeof(PdfString))]
        [InlineData((int)PdfKeyType.Boolean, typeof(PdfBoolean))]
        [InlineData((int)PdfKeyType.Integer, typeof(PdfInteger))]
        [InlineData((int)PdfKeyType.Real, typeof(PdfReal))]
        [InlineData((int)PdfKeyType.Date, typeof(PdfDate))]
        [InlineData((int)PdfKeyType.Rectangle, typeof(PdfRectangle))]
        [InlineData((int)PdfKeyType.Array, typeof(PdfArray))]
        [InlineData((int)PdfKeyType.Dictionary, typeof(PdfDictionary))]
        [InlineData((int)PdfKeyType.Stream, typeof(PdfDictionary))]
        public void GetValueType_KnownKeyType_ReturnsMappedType(int keyTypeValue, Type expected)
        {
            var descriptor = CreateDescriptor((PdfKeyType)keyTypeValue);

            Assert.Equal(expected, descriptor.GetValueType());
        }

        [Fact]
        public void GetValueType_ExplicitObjectType_ReturnsItDirectlyWithoutConsultingKeyType()
        {
            var descriptor = new PdfKeyDescriptor(new PdfKeyAttribute(PdfKeyType.Name, typeof(PdfArray)));

            Assert.Equal(typeof(PdfArray), descriptor.GetValueType());
        }

        [Theory]
        [InlineData((int)PdfKeyType.NumberTree)]
        [InlineData((int)PdfKeyType.NameOrArray)]
        [InlineData((int)PdfKeyType.ArrayOrDictionary)]
        [InlineData((int)PdfKeyType.StreamOrArray)]
        public void GetValueType_NotYetImplementedKeyType_Throws(int keyTypeValue)
        {
            var descriptor = CreateDescriptor((PdfKeyType)keyTypeValue);

            Assert.Throws<NotImplementedException>(() => descriptor.GetValueType());
        }

        [Fact]
        public void GetValueType_ArrayOrNameOrString_ReturnsNull()
        {
            // HACK carried over from the original switch: PdfOutline relies on this returning null
            // rather than throwing.
            var descriptor = CreateDescriptor(PdfKeyType.ArrayOrNameOrString);

            Assert.Null(descriptor.GetValueType());
        }

    }
}
