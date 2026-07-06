using PeachPDF.PdfSharpCore.Pdf;
using System;
using System.Globalization;

namespace PeachPDF.Tests.PdfSharpCoreTests.Pdf
{
    public class PdfIntegerTests
    {
        [Fact]
        public void DefaultConstructor_ValueIsZero()
        {
            Assert.Equal(0, new PdfInteger().Value);
        }

        [Fact]
        public void Constructor_SetsValue()
        {
            Assert.Equal(42, new PdfInteger(42).Value);
        }

        [Fact]
        public void ToString_ReturnsInvariantCultureRepresentation()
        {
            Assert.Equal("42", new PdfInteger(42).ToString());
            Assert.Equal("-7", new PdfInteger(-7).ToString());
        }

        [Fact]
        public void GetTypeCode_ReturnsInt32()
        {
            IConvertible convertible = new PdfInteger(1);

            Assert.Equal(TypeCode.Int32, convertible.GetTypeCode());
        }

        [Fact]
        public void IConvertible_NumericConversions_MatchValue()
        {
            IConvertible convertible = new PdfInteger(42);

            Assert.Equal((ulong)42, convertible.ToUInt64(CultureInfo.InvariantCulture));
            Assert.Equal(42d, convertible.ToDouble(CultureInfo.InvariantCulture));
            Assert.Equal(42f, convertible.ToSingle(CultureInfo.InvariantCulture));
            Assert.Equal(42, convertible.ToInt32(CultureInfo.InvariantCulture));
            Assert.Equal((ushort)42, convertible.ToUInt16(CultureInfo.InvariantCulture));
            Assert.Equal((short)42, convertible.ToInt16(CultureInfo.InvariantCulture));
            Assert.Equal((byte)42, convertible.ToByte(CultureInfo.InvariantCulture));
            Assert.Equal(42L, convertible.ToInt64(CultureInfo.InvariantCulture));
            Assert.Equal(42m, convertible.ToDecimal(CultureInfo.InvariantCulture));
            Assert.Equal((uint)42, convertible.ToUInt32(CultureInfo.InvariantCulture));
        }

        [Fact]
        public void IConvertible_ToString_UsesProvider()
        {
            IConvertible convertible = new PdfInteger(42);

            Assert.Equal("42", convertible.ToString(CultureInfo.InvariantCulture));
        }

        [Fact]
        public void IConvertible_ToBoolean_ZeroIsFalseNonZeroIsTrue()
        {
            Assert.False(((IConvertible)new PdfInteger(0)).ToBoolean(CultureInfo.InvariantCulture));
            Assert.True(((IConvertible)new PdfInteger(1)).ToBoolean(CultureInfo.InvariantCulture));
        }

        [Fact]
        public void IConvertible_ToChar_ConvertsCodepoint()
        {
            IConvertible convertible = new PdfInteger('A');

            Assert.Equal('A', convertible.ToChar(CultureInfo.InvariantCulture));
        }

        [Fact]
        public void IConvertible_ToSByte_Throws()
        {
            IConvertible convertible = new PdfInteger(1);

            Assert.Throws<InvalidCastException>(() => convertible.ToSByte(CultureInfo.InvariantCulture));
        }

        [Fact]
        public void IConvertible_ToDateTime_ReturnsDefaultDateTime()
        {
            IConvertible convertible = new PdfInteger(1);

            Assert.Equal(new DateTime(), convertible.ToDateTime(CultureInfo.InvariantCulture));
        }

        [Fact]
        public void IConvertible_ToType_ReturnsNull()
        {
            IConvertible convertible = new PdfInteger(1);

            Assert.Null(convertible.ToType(typeof(int), CultureInfo.InvariantCulture));
        }

        [Fact]
        public void IFormattable_ToString_ReturnsFormatArgumentVerbatim()
        {
            IFormattable formattable = new PdfInteger(42);

            Assert.Equal("N0", formattable.ToString("N0", CultureInfo.InvariantCulture));
        }
    }
}
