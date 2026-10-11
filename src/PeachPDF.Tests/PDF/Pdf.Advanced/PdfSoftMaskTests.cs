using PeachPDF.PDF;
using PeachPDF.PDF.Advanced;

namespace PeachPDF.Tests.PdfEngineTests.Pdf.Advanced
{
    public class PdfSoftMaskTests
    {
        [Fact]
        public void TransferFunction_StoredAsIndirectReference()
        {
            var document = new PdfDocument();
            var softMask = new PdfSoftMask(document);
            var invert = PdfType4Function.BuildInvertFunction(document);

            softMask.TransferFunction = invert;

            var stored = softMask.Elements[PdfSoftMask.Keys.TR];
            var reference = Assert.IsType<PdfReference>(stored);
            Assert.Same(invert, reference.Value);
        }

        [Fact]
        public void WithoutTransferFunction_TRKeyIsAbsent()
        {
            var document = new PdfDocument();
            var softMask = new PdfSoftMask(document);

            Assert.False(softMask.Elements.ContainsKey(PdfSoftMask.Keys.TR));
        }
    }
}
