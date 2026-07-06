using PeachPDF.PdfSharpCore.Pdf;
using System;

namespace PeachPDF.Tests.PdfSharpCoreTests.Pdf
{
    public class PdfNameTests
    {
        [Fact]
        public void DefaultConstructor_ProducesEmptyName()
        {
            Assert.Equal("/", new PdfName().Value);
        }

        [Fact]
        public void Constructor_SetsValue()
        {
            Assert.Equal("/Type", new PdfName("/Type").Value);
        }

        [Fact]
        public void Constructor_NullValue_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new PdfName(null!));
        }

        [Fact]
        public void Constructor_EmptyValue_Throws()
        {
            Assert.Throws<ArgumentException>(() => new PdfName(""));
        }

        [Fact]
        public void Constructor_ValueWithoutLeadingSlash_Throws()
        {
            Assert.Throws<ArgumentException>(() => new PdfName("Type"));
        }

        [Fact]
        public void ToString_ReturnsValueWithSlash()
        {
            Assert.Equal("/Type", new PdfName("/Type").ToString());
        }

        [Fact]
        public void EqualityOperators_CompareUnderlyingString()
        {
            var name = new PdfName("/Type");

            Assert.True(name == "/Type");
            Assert.False(name == "/Other");
            Assert.True(name != "/Other");
            Assert.False(name != "/Type");
        }

        [Fact]
        public void EqualityOperator_NullName_ComparesAgainstNullString()
        {
            PdfName? name = null;

            Assert.True(name == null);
            Assert.False(name != null);
        }

        [Fact]
        public void Equals_ComparesUnderlyingString()
        {
            var name = new PdfName("/Type");

            Assert.True(name.Equals("/Type"));
            Assert.False(name.Equals("/Other"));
        }

        [Fact]
        public void GetHashCode_MatchesStringHashCode()
        {
            var name = new PdfName("/Type");

            Assert.Equal("/Type".GetHashCode(), name.GetHashCode());
        }

        [Fact]
        public void Empty_IsSlashOnly()
        {
            Assert.Equal("/", PdfName.Empty.Value);
        }

        [Fact]
        public void Comparer_OrdersNamesLexically()
        {
            var comparer = PdfName.Comparer;
            var a = new PdfName("/A");
            var b = new PdfName("/B");

            Assert.True(comparer.Compare(a, b) < 0);
            Assert.True(comparer.Compare(b, a) > 0);
            Assert.Equal(0, comparer.Compare(a, new PdfName("/A")));
            Assert.Equal(-1, comparer.Compare(a, null));
            Assert.Equal(1, comparer.Compare(null, a));
            Assert.Equal(0, comparer.Compare(null, null));
        }
    }
}
