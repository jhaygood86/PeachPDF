using PeachPDF.PdfSharpCore.Pdf;
using System;
using Xunit;

namespace PeachPDF.Tests.PdfSharpCoreTests.Pdf
{
    public class KeyDescriptorTests
    {
        private static KeyDescriptor CreateDescriptor(KeyType keyType) =>
            new KeyDescriptor(new KeyInfoAttribute(keyType));

        [Theory]
        [InlineData((int)KeyType.Name, typeof(PdfName))]
        [InlineData((int)KeyType.String, typeof(PdfString))]
        [InlineData((int)KeyType.Boolean, typeof(PdfBoolean))]
        [InlineData((int)KeyType.Integer, typeof(PdfInteger))]
        [InlineData((int)KeyType.Real, typeof(PdfReal))]
        [InlineData((int)KeyType.Date, typeof(PdfDate))]
        [InlineData((int)KeyType.Rectangle, typeof(PdfRectangle))]
        [InlineData((int)KeyType.Array, typeof(PdfArray))]
        [InlineData((int)KeyType.Dictionary, typeof(PdfDictionary))]
        [InlineData((int)KeyType.Stream, typeof(PdfDictionary))]
        public void GetValueType_KnownKeyType_ReturnsMappedType(int keyTypeValue, Type expected)
        {
            var descriptor = CreateDescriptor((KeyType)keyTypeValue);

            Assert.Equal(expected, descriptor.GetValueType());
        }

        [Fact]
        public void GetValueType_ExplicitObjectType_ReturnsItDirectlyWithoutConsultingKeyType()
        {
            var descriptor = new KeyDescriptor(new KeyInfoAttribute(KeyType.Name, typeof(PdfArray)));

            Assert.Equal(typeof(PdfArray), descriptor.GetValueType());
        }

        [Theory]
        [InlineData((int)KeyType.NumberTree)]
        [InlineData((int)KeyType.NameOrArray)]
        [InlineData((int)KeyType.ArrayOrDictionary)]
        [InlineData((int)KeyType.StreamOrArray)]
        public void GetValueType_NotYetImplementedKeyType_Throws(int keyTypeValue)
        {
            var descriptor = CreateDescriptor((KeyType)keyTypeValue);

            Assert.Throws<NotImplementedException>(() => descriptor.GetValueType());
        }

        [Fact]
        public void GetValueType_ArrayOrNameOrString_ReturnsNull()
        {
            // HACK carried over from the original switch: PdfOutline relies on this returning null
            // rather than throwing.
            var descriptor = CreateDescriptor(KeyType.ArrayOrNameOrString);

            Assert.Null(descriptor.GetValueType());
        }

    }
}
