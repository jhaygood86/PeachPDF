using PeachPDF.Html.Adapters.Entities;
using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.PdfSharpCore.Pdf;
using PeachPDF.PdfSharpCore.Pdf.Advanced;
using System;
using System.IO;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;

namespace PeachPDF.Tests.PdfSharpCoreTests.Drawing.Pdf
{
    /// <summary>
    /// Structural/adjacency tests for the new tile-compositing primitives added alongside CSS/SVG
    /// filter support - <c>DrawImageWithColorMatrix</c>, <c>DrawImageAlphaMasked</c> and
    /// <c>DrawImageBlendedOver</c>. Per this repo's testing convention, a content-stream substring match
    /// alone is not proof of correct behavior for anything touching soft masks/transfer functions/blend
    /// modes - these assert the specific adjacency (the activating <c>gs</c> and the <c>Do</c> it modifies
    /// sharing one <c>cm</c> line) that made <c>DrawImageMasked</c>/<c>DrawImageWithOpacity</c> actually
    /// work, plus the presence of the underlying PDF constructs (Type 4 function, mask subtype, /BM).
    /// A real end-to-end visual (dual-rasterized) confirmation for the channel-independent color-matrix
    /// path is covered separately, outside the unit-test suite, since it needs Python/PDFium/MuPDF.
    /// </summary>
    public class XGraphicsPdfRendererColorMatrixAndMaskTests
    {
        private static (PdfDocument Document, XGraphics Gfx) NewPage()
        {
            var document = new PdfDocument();
            document.Options.CompressContentStreams = false;
            var page = document.AddPage();
            var gfx = XGraphics.FromPdfPage(page);
            return (document, gfx);
        }

        private static XForm NewTile(PdfDocument document, double size, XBrush brush)
        {
            var form = new XForm(document, new XSize(size, size));
            var formGfx = XGraphics.FromForm(form);
            formGfx.DrawRectangle(brush, 0, 0, size, size);
            return form;
        }

        private static string Serialize(PdfDocument document)
        {
            var ms = new MemoryStream();
            document.Save(ms);
            return Encoding.Latin1.GetString(ms.ToArray());
        }

        // A uniform 150% brightness scale - diagonal, so channel-independent and /TR-representable.
        private static readonly ColorMatrix Brightness150 = new(new Matrix4x4(
            1.5f, 0, 0, 0,
            0, 1.5f, 0, 0,
            0, 0, 1.5f, 0,
            0, 0, 0, 1), Vector4.Zero);

        // Grayscale luminance weights - the textbook cross-channel matrix, not representable via /TR.
        private static readonly ColorMatrix Grayscale = new(new Matrix4x4(
            0.2126f, 0.2126f, 0.2126f, 0,
            0.7152f, 0.7152f, 0.7152f, 0,
            0.0722f, 0.0722f, 0.0722f, 0,
            0, 0, 0, 1), Vector4.Zero);

        [Fact]
        public void DrawImageWithColorMatrix_ChannelIndependent_GsAndDoShareTheSameCm()
        {
            var (document, gfx) = NewPage();
            var form = NewTile(document, 50, XBrushes.Red);

            gfx.DrawImageWithColorMatrix(form, new XRect(10, 10, 50, 50), Brightness150);
            gfx.Dispose();

            var text = Serialize(document);
            Assert.Matches(new Regex(@"cm /GS\d+ gs /Fm\d+ Do"), text);
        }

        [Fact]
        public void DrawImageWithColorMatrix_ChannelIndependent_EmitsType4TransferFunction()
        {
            var (document, gfx) = NewPage();
            var form = NewTile(document, 50, XBrushes.Red);

            gfx.DrawImageWithColorMatrix(form, new XRect(10, 10, 50, 50), Brightness150);
            gfx.Dispose();

            var text = Serialize(document);
            Assert.Contains("/FunctionType 4", text);
            Assert.Contains("/TR", text);
        }

        [Fact]
        public void DrawImageWithColorMatrix_CrossChannel_Throws()
        {
            var (document, gfx) = NewPage();
            var form = NewTile(document, 50, XBrushes.Red);

            var ex = Assert.Throws<NotSupportedException>(() =>
                gfx.DrawImageWithColorMatrix(form, new XRect(10, 10, 50, 50), Grayscale));

            Assert.Contains("color channel", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void DrawImageAlphaMasked_GsAndDoShareTheSameCm()
        {
            var (document, gfx) = NewPage();
            var content = NewTile(document, 50, XBrushes.Red);
            var mask = NewTile(document, 50, XBrushes.White);

            gfx.DrawImageAlphaMasked(content, mask, new XRect(10, 10, 50, 50));
            gfx.Dispose();

            var text = Serialize(document);
            Assert.Matches(new Regex(@"cm /GS\d+ gs /Fm\d+ Do"), text);
        }

        [Fact]
        public void DrawImageAlphaMasked_UsesAlphaSubtype_NotLuminosity()
        {
            var (document, gfx) = NewPage();
            var content = NewTile(document, 50, XBrushes.Red);
            var mask = NewTile(document, 50, XBrushes.White);

            gfx.DrawImageAlphaMasked(content, mask, new XRect(10, 10, 50, 50));
            gfx.Dispose();

            var text = Serialize(document);
            Assert.Contains("/S /Alpha", text);
            Assert.DoesNotContain("/S /Luminosity", text);
        }

        [Fact]
        public void DrawImageAlphaMasked_Invert_AddsTransferFunctionToTheMask()
        {
            var (documentPlain, gfxPlain) = NewPage();
            var contentPlain = NewTile(documentPlain, 50, XBrushes.Red);
            var maskPlain = NewTile(documentPlain, 50, XBrushes.White);
            gfxPlain.DrawImageAlphaMasked(contentPlain, maskPlain, new XRect(10, 10, 50, 50));
            gfxPlain.Dispose();
            var plainFunctionCount = Regex.Matches(Serialize(documentPlain), "/FunctionType 4").Count;

            var (documentInverted, gfxInverted) = NewPage();
            var contentInverted = NewTile(documentInverted, 50, XBrushes.Red);
            var maskInverted = NewTile(documentInverted, 50, XBrushes.White);
            gfxInverted.DrawImageAlphaMasked(contentInverted, maskInverted, new XRect(10, 10, 50, 50), invert: true);
            gfxInverted.Dispose();
            var invertedFunctionCount = Regex.Matches(Serialize(documentInverted), "/FunctionType 4").Count;

            Assert.Equal(0, plainFunctionCount);
            Assert.Equal(1, invertedFunctionCount);
        }

        [Fact]
        public void DrawImageBlendedOver_PaintsBottomThenTopWithBlendModeOnTopsCm()
        {
            var (document, gfx) = NewPage();
            var top = NewTile(document, 50, XBrushes.Red);
            var bottom = NewTile(document, 50, XBrushes.Blue);

            gfx.DrawImageBlendedOver(top, bottom, new XRect(10, 10, 50, 50), "Multiply");
            gfx.Dispose();

            var text = Serialize(document);

            // bottom's own placement carries no gs at all (painted normally); top's placement carries
            // the /BM Multiply gs on the same cm line as its own Do - not applied separately/globally.
            Assert.Matches(new Regex(@"cm /Fm\d+ Do"), text);
            Assert.Matches(new Regex(@"cm /GS\d+ gs /Fm\d+ Do"), text);
            Assert.Contains("/BM /Multiply", text);
        }

        [Fact]
        public void DrawImageBlendedOver_NonNormalBlendMode_RejectedUnderPdfA1()
        {
            // Mirrors SetBlendMode's own carve-out (a non-Normal /BM is the transparency-group-
            // requiring construct PDF/A-1 forbids) - DrawImageBlendedOver must apply the same check,
            // not reject every feBlend call unconditionally regardless of mode.
            var document = new PdfDocument();
            document.Options.PdfAConformance = PdfAConformance.PdfA1B;
            var page = document.AddPage();
            using var gfx = XGraphics.FromPdfPage(page);
            var top = NewTile(document, 50, XBrushes.Red);
            var bottom = NewTile(document, 50, XBrushes.Blue);

            Assert.Throws<PdfAConformanceException>(() =>
                gfx.DrawImageBlendedOver(top, bottom, new XRect(10, 10, 50, 50), "Multiply"));
        }

        [Fact]
        public void DrawImageBlendedOver_NormalBlendMode_AllowedUnderPdfA1()
        {
            var document = new PdfDocument();
            document.Options.PdfAConformance = PdfAConformance.PdfA1B;
            var page = document.AddPage();
            using var gfx = XGraphics.FromPdfPage(page);
            var top = NewTile(document, 50, XBrushes.Red);
            var bottom = NewTile(document, 50, XBrushes.Blue);

            // Should not throw - a Normal-mode feBlend is just two ordinary Form XObject placements.
            gfx.DrawImageBlendedOver(top, bottom, new XRect(10, 10, 50, 50), "Normal");
        }

        [Fact]
        public void DrawImageWithColorMatrix_RejectedUnderPdfA1()
        {
            var document = new PdfDocument();
            document.Options.PdfAConformance = PdfAConformance.PdfA1B;
            var page = document.AddPage();
            using var gfx = XGraphics.FromPdfPage(page);
            var form = NewTile(document, 50, XBrushes.Red);

            Assert.Throws<PdfAConformanceException>(() =>
                gfx.DrawImageWithColorMatrix(form, new XRect(10, 10, 50, 50), Brightness150));
        }

        [Fact]
        public void DrawImageAlphaMasked_RejectedUnderPdfA1()
        {
            var document = new PdfDocument();
            document.Options.PdfAConformance = PdfAConformance.PdfA1B;
            var page = document.AddPage();
            using var gfx = XGraphics.FromPdfPage(page);
            var content = NewTile(document, 50, XBrushes.Red);
            var mask = NewTile(document, 50, XBrushes.White);

            Assert.Throws<PdfAConformanceException>(() =>
                gfx.DrawImageAlphaMasked(content, mask, new XRect(10, 10, 50, 50)));
        }

        [Fact]
        public void DrawImageBlendedOver_NormalBlendMode_StillPaintsBothForms()
        {
            var (document, gfx) = NewPage();
            var top = NewTile(document, 50, XBrushes.Red);
            var bottom = NewTile(document, 50, XBrushes.Blue);

            gfx.DrawImageBlendedOver(top, bottom, new XRect(10, 10, 50, 50), "Normal");
            gfx.Dispose();

            var text = Serialize(document);
            // "Do" alone as a substring also matches inside unrelated text (e.g. the PDF producer
            // comment's "PdfDocumentInformation") - anchor on the actual "/Fm<n> Do" placement operator.
            var doCount = Regex.Matches(text, @"/Fm\d+ Do").Count;
            Assert.Equal(2, doCount);
        }
    }
}
