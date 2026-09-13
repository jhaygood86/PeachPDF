using PeachPDF.PdfSharpCore.Pdf;
using PeachPDF.PdfSharpCore.Pdf.Advanced;

namespace PeachPDF.Tests.PdfSharpCoreTests.Pdf.Advanced
{
    public class PdfExtGStateTableTests
    {
        [Fact]
        public void GetExtGState_WithNormalBlendMode_IsSameCachedInstanceAsSingleArgOverload()
        {
            var document = new PdfDocument();

            var withNormal = document.ExtGStateTable.GetExtGState(0.5, "Normal");
            var singleArg = document.ExtGStateTable.GetExtGState(0.5);

            Assert.Same(singleArg, withNormal);
            Assert.False(withNormal.Elements.ContainsKey(PdfExtGState.Keys.BM));
        }

        [Fact]
        public void GetExtGState_WithNullBlendMode_IsSameCachedInstanceAsSingleArgOverload()
        {
            var document = new PdfDocument();

            var withNull = document.ExtGStateTable.GetExtGState(0.5, null!);
            var singleArg = document.ExtGStateTable.GetExtGState(0.5);

            Assert.Same(singleArg, withNull);
        }

        [Fact]
        public void GetExtGState_WithNonNormalBlendMode_SetsBMAndAlpha()
        {
            var document = new PdfDocument();

            var extGState = document.ExtGStateTable.GetExtGState(0.5, "Multiply");

            Assert.Equal("/Multiply", extGState.Elements.GetName(PdfExtGState.Keys.BM));
            Assert.Equal(0.5, extGState.Elements.GetReal(PdfExtGState.Keys.ca));
            Assert.Equal(0.5, extGState.Elements.GetReal(PdfExtGState.Keys.CA));
        }

        [Fact]
        public void GetExtGState_SameAlphaAndBlendMode_ReturnsCachedInstance()
        {
            var document = new PdfDocument();

            var first = document.ExtGStateTable.GetExtGState(0.75, "Screen");
            var second = document.ExtGStateTable.GetExtGState(0.75, "Screen");

            Assert.Same(first, second);
        }

        [Fact]
        public void GetExtGState_DifferentBlendModes_ReturnsDistinctInstances()
        {
            var document = new PdfDocument();

            var multiply = document.ExtGStateTable.GetExtGState(0.75, "Multiply");
            var screen = document.ExtGStateTable.GetExtGState(0.75, "Screen");

            Assert.NotSame(multiply, screen);
        }
    }
}
