using PeachPDF.Html.Adapters.Entities;
using PeachPDF.PdfSharpCore.Pdf;
using PeachPDF.PdfSharpCore.Pdf.Advanced;
using System.Numerics;

namespace PeachPDF.Tests.PdfSharpCoreTests.Pdf.Advanced
{
    public class PdfExtGStateTests
    {
        [Fact]
        public void TransferFunction_SingleFunction_StoredAsIndirectReference()
        {
            var document = new PdfDocument();
            var extGState = new PdfExtGState(document);
            var fn = PdfType4Function.BuildInvertFunction(document);

            extGState.TransferFunction = fn;

            var stored = extGState.Elements[PdfExtGState.Keys.TR];
            var reference = Assert.IsType<PdfReference>(stored);
            Assert.Same(fn, reference.Value);
        }

        [Fact]
        public void TransferFunction_Array_StoredAsDirectArrayOfReferences()
        {
            // A channel-independent matrix whose three channels differ builds an array (see
            // PdfType4FunctionTests) - the array value itself is embedded directly (not itself made
            // indirect), while each element inside it is an indirect reference to its own function.
            var document = new PdfDocument();
            var extGState = new PdfExtGState(document);
            var matrix = new ColorMatrix(
                new Matrix4x4(
                    2, 0, 0, 0,
                    0, 3, 0, 0,
                    0, 0, 4, 0,
                    0, 0, 0, 1),
                Vector4.Zero);
            var transferFunction = PdfType4Function.BuildChannelIndependentTransferFunction(document, matrix);

            extGState.TransferFunction = transferFunction;

            var stored = extGState.Elements[PdfExtGState.Keys.TR];
            var array = Assert.IsType<PdfArray>(stored);
            Assert.Equal(3, array.Elements.Count);
            Assert.All(array.Elements, e => Assert.IsType<PdfReference>(e));
        }

        [Fact]
        public void Keys_TRAndTR2_AreDistinctRealKeys()
        {
            Assert.Equal("/TR", PdfExtGState.Keys.TR);
            Assert.Equal("/TR2", PdfExtGState.Keys.TR2);
        }
    }
}
