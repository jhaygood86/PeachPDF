using PeachDrawing.Core;
using PeachDrawing.PDF.Engine.Drawing;
using PeachDrawing.PDF.Engine;
using PeachDrawing.PDF.Engine.Advanced;
using System;
using System.IO;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;

namespace PeachPDF.Tests.PdfEngineTests.Rendering
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
    public class PdfGraphicsRendererColorMatrixAndMaskTests
    {
        private static (PdfDocument Document, PdfGraphics Gfx) NewPage()
        {
            var document = new PdfDocument();
            document.Options.CompressContentStreams = false;
            var page = document.AddPage();
            var gfx = PdfGraphics.FromPdfPage(page);
            return (document, gfx);
        }

        private static PdfForm NewTile(PdfDocument document, double size, Brush brush)
        {
            var form = new PdfForm(document, new Size(size, size));
            var formGfx = PdfGraphics.FromForm(form);
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
            var form = NewTile(document, 50, new SolidBrush(PaintColor.FromArgb(255, 0, 0)));

            gfx.DrawImageWithColorMatrix(form, new Rect(10, 10, 50, 50), Brightness150);
            gfx.Dispose();

            var text = Serialize(document);
            Assert.Matches(new Regex(@"cm /GS\d+ gs /Fm\d+ Do"), text);
        }

        [Fact]
        public void DrawImageWithColorMatrix_ChannelIndependent_EmitsType4TransferFunction()
        {
            var (document, gfx) = NewPage();
            var form = NewTile(document, 50, new SolidBrush(PaintColor.FromArgb(255, 0, 0)));

            gfx.DrawImageWithColorMatrix(form, new Rect(10, 10, 50, 50), Brightness150);
            gfx.Dispose();

            var text = Serialize(document);
            Assert.Contains("/FunctionType 4", text);
            Assert.Contains("/TR", text);
        }

        [Fact]
        public void DrawImageWithColorMatrix_CrossChannel_Throws()
        {
            var (document, gfx) = NewPage();
            var form = NewTile(document, 50, new SolidBrush(PaintColor.FromArgb(255, 0, 0)));

            var ex = Assert.Throws<NotSupportedException>(() =>
                gfx.DrawImageWithColorMatrix(form, new Rect(10, 10, 50, 50), Grayscale));

            Assert.Contains("color channel", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void DrawImageAlphaMasked_GsAndDoShareTheSameCm()
        {
            var (document, gfx) = NewPage();
            var content = NewTile(document, 50, new SolidBrush(PaintColor.FromArgb(255, 0, 0)));
            var mask = NewTile(document, 50, new SolidBrush(PaintColor.White));

            gfx.DrawImageAlphaMasked(content, mask, new Rect(10, 10, 50, 50));
            gfx.Dispose();

            var text = Serialize(document);
            Assert.Matches(new Regex(@"cm /GS\d+ gs /Fm\d+ Do"), text);
        }

        [Fact]
        public void DrawImageAlphaMasked_UsesAlphaSubtype_NotLuminosity()
        {
            var (document, gfx) = NewPage();
            var content = NewTile(document, 50, new SolidBrush(PaintColor.FromArgb(255, 0, 0)));
            var mask = NewTile(document, 50, new SolidBrush(PaintColor.White));

            gfx.DrawImageAlphaMasked(content, mask, new Rect(10, 10, 50, 50));
            gfx.Dispose();

            var text = Serialize(document);
            Assert.Contains("/S /Alpha", text);
            Assert.DoesNotContain("/S /Luminosity", text);
        }

        [Fact]
        public void DrawImageAlphaMasked_Invert_AddsTransferFunctionToTheMask()
        {
            var (documentPlain, gfxPlain) = NewPage();
            var contentPlain = NewTile(documentPlain, 50, new SolidBrush(PaintColor.FromArgb(255, 0, 0)));
            var maskPlain = NewTile(documentPlain, 50, new SolidBrush(PaintColor.White));
            gfxPlain.DrawImageAlphaMasked(contentPlain, maskPlain, new Rect(10, 10, 50, 50));
            gfxPlain.Dispose();
            var plainFunctionCount = Regex.Matches(Serialize(documentPlain), "/FunctionType 4").Count;

            var (documentInverted, gfxInverted) = NewPage();
            var contentInverted = NewTile(documentInverted, 50, new SolidBrush(PaintColor.FromArgb(255, 0, 0)));
            var maskInverted = NewTile(documentInverted, 50, new SolidBrush(PaintColor.White));
            gfxInverted.DrawImageAlphaMasked(contentInverted, maskInverted, new Rect(10, 10, 50, 50), invert: true);
            gfxInverted.Dispose();
            var invertedFunctionCount = Regex.Matches(Serialize(documentInverted), "/FunctionType 4").Count;

            Assert.Equal(0, plainFunctionCount);
            Assert.Equal(1, invertedFunctionCount);
        }

        [Fact]
        public void DrawImageBlendedOver_PaintsBottomThenTopWithBlendModeOnTopsCm()
        {
            var (document, gfx) = NewPage();
            var top = NewTile(document, 50, new SolidBrush(PaintColor.FromArgb(255, 0, 0)));
            var bottom = NewTile(document, 50, new SolidBrush(PaintColor.FromArgb(0, 0, 255)));

            gfx.DrawImageBlendedOver(top, bottom, new Rect(10, 10, 50, 50), "Multiply");
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
            document.Options.PdfAConformance = PeachDrawing.PDF.Engine.PdfAConformance.PdfA1B;
            var page = document.AddPage();
            using var gfx = PdfGraphics.FromPdfPage(page);
            var top = NewTile(document, 50, new SolidBrush(PaintColor.FromArgb(255, 0, 0)));
            var bottom = NewTile(document, 50, new SolidBrush(PaintColor.FromArgb(0, 0, 255)));

            Assert.Throws<PdfAConformanceException>(() =>
                gfx.DrawImageBlendedOver(top, bottom, new Rect(10, 10, 50, 50), "Multiply"));
        }

        [Fact]
        public void DrawImageBlendedOver_NormalBlendMode_AllowedUnderPdfA1()
        {
            var document = new PdfDocument();
            document.Options.PdfAConformance = PeachDrawing.PDF.Engine.PdfAConformance.PdfA1B;
            var page = document.AddPage();
            using var gfx = PdfGraphics.FromPdfPage(page);
            var top = NewTile(document, 50, new SolidBrush(PaintColor.FromArgb(255, 0, 0)));
            var bottom = NewTile(document, 50, new SolidBrush(PaintColor.FromArgb(0, 0, 255)));

            // Should not throw - a Normal-mode feBlend is just two ordinary Form XObject placements.
            gfx.DrawImageBlendedOver(top, bottom, new Rect(10, 10, 50, 50), "Normal");
        }

        [Fact]
        public void DrawImageWithColorMatrix_RejectedUnderPdfA1()
        {
            var document = new PdfDocument();
            document.Options.PdfAConformance = PeachDrawing.PDF.Engine.PdfAConformance.PdfA1B;
            var page = document.AddPage();
            using var gfx = PdfGraphics.FromPdfPage(page);
            var form = NewTile(document, 50, new SolidBrush(PaintColor.FromArgb(255, 0, 0)));

            Assert.Throws<PdfAConformanceException>(() =>
                gfx.DrawImageWithColorMatrix(form, new Rect(10, 10, 50, 50), Brightness150));
        }

        private static (PdfDocument Document, PdfGraphics Gfx) NewPortablePage()
        {
            var (document, gfx) = NewPage();
            document.Options.PortableColorFilters = true;
            return (document, gfx);
        }

        [Fact]
        public void PortableColorFilters_WriteBlendLayersThroughAnAlphaMask_NotATransferFunction()
        {
            var (document, gfx) = NewPortablePage();
            var form = NewTile(document, 50, new SolidBrush(PaintColor.FromArgb(255, 0, 0)));

            gfx.DrawImageWithColorMatrix(form, new Rect(10, 10, 50, 50), Brightness150);
            gfx.Dispose();

            var text = Serialize(document);
            Assert.DoesNotContain("/TR", text);
            Assert.DoesNotContain("/FunctionType 4", text);
            Assert.Contains("/BM /ColorDodge", text);
            Assert.Contains("/S /Alpha", text);
            // The soft mask and the wrapper it applies to are placed by one cm/gs/Do, like every other tile composite.
            Assert.Matches(new Regex(@"cm /GS\d+ gs /Fm\d+ Do"), text);
        }

        [Fact]
        public void PortableColorFilters_PaintTheContentBeforeTheFillsThatBlendWithIt()
        {
            var (document, gfx) = NewPortablePage();
            var form = NewTile(document, 50, new SolidBrush(PaintColor.FromArgb(255, 0, 0)));

            // contrast(2): ColorDodge then ColorBurn, in that order.
            var contrast = new ColorMatrix(new Matrix4x4(
                2, 0, 0, 0,
                0, 2, 0, 0,
                0, 0, 2, 0,
                0, 0, 0, 1), new Vector4(-0.5f, -0.5f, -0.5f, 0));
            gfx.DrawImageWithColorMatrix(form, new Rect(10, 10, 50, 50), contrast);
            gfx.Dispose();

            var text = Serialize(document);
            Assert.Contains("/BM /ColorDodge", text);
            Assert.Contains("/BM /ColorBurn", text);

            // The wrapper's own content stream: the tile first, then a gs and a full-tile fill for each layer.
            Assert.Matches(new Regex(@"/Fm\d+ Do\s+Q[\s\S]*?/GS\d+ gs[\s\S]*?re[\s\S]*?f[\s\S]*?/GS\d+ gs[\s\S]*?re[\s\S]*?f"), text);
        }

        [Fact]
        public void PortableColorFilters_BothTheWrapperAndTheMaskedContentAreTransparencyGroups()
        {
            var (document, gfx) = NewPortablePage();
            var form = NewTile(document, 50, new SolidBrush(PaintColor.FromArgb(255, 0, 0)));

            gfx.DrawImageWithColorMatrix(form, new Rect(10, 10, 50, 50), Brightness150);
            gfx.Dispose();

            // PDFium ignores an active SMask unless the masked form (the wrapper) and the /G form are groups.
            Assert.True(Regex.Matches(Serialize(document), @"/S /Transparency").Count >= 2);
        }

        [Fact]
        public void PortableColorFilters_AMatrixThatChangesNothing_DrawsTheTilePlainly()
        {
            var (document, gfx) = NewPortablePage();
            var form = NewTile(document, 50, new SolidBrush(PaintColor.FromArgb(255, 0, 0)));

            gfx.DrawImageWithColorMatrix(form, new Rect(10, 10, 50, 50), ColorMatrix.Identity);
            gfx.Dispose();

            var text = Serialize(document);
            Assert.DoesNotContain("/BM /", text);
            Assert.DoesNotContain("/SMask", text);
            Assert.DoesNotContain("/TR", text);
        }

        [Fact]
        public void PortableColorFilters_CrossChannelMatrix_StillThrows()
        {
            var (document, gfx) = NewPortablePage();
            var form = NewTile(document, 50, new SolidBrush(PaintColor.FromArgb(255, 0, 0)));

            Assert.Throws<NotSupportedException>(() =>
                gfx.DrawImageWithColorMatrix(form, new Rect(10, 10, 50, 50), Grayscale));
        }

        [Fact]
        public void PortableColorFilters_RejectedUnderPdfA1()
        {
            var document = new PdfDocument();
            document.Options.PdfAConformance = PeachDrawing.PDF.Engine.PdfAConformance.PdfA1B;
            document.Options.PortableColorFilters = true;
            var page = document.AddPage();
            using var gfx = PdfGraphics.FromPdfPage(page);
            var form = NewTile(document, 50, new SolidBrush(PaintColor.FromArgb(255, 0, 0)));

            Assert.Throws<PdfAConformanceException>(() =>
                gfx.DrawImageWithColorMatrix(form, new Rect(10, 10, 50, 50), Brightness150));
        }

        [Fact]
        public void PortableColorFilters_AMatrixThatChangesNothing_IsStillRejectedUnderPdfA1()
        {
            var document = new PdfDocument();
            document.Options.PdfAConformance = PeachDrawing.PDF.Engine.PdfAConformance.PdfA1B;
            document.Options.PortableColorFilters = true;
            var page = document.AddPage();
            using var gfx = PdfGraphics.FromPdfPage(page);
            var form = NewTile(document, 50, new SolidBrush(PaintColor.FromArgb(255, 0, 0)));

            Assert.Throws<PdfAConformanceException>(() =>
                gfx.DrawImageWithColorMatrix(form, new Rect(10, 10, 50, 50), ColorMatrix.Identity));
        }

        [Fact]
        public void DrawImageAlphaMasked_RejectedUnderPdfA1()
        {
            var document = new PdfDocument();
            document.Options.PdfAConformance = PeachDrawing.PDF.Engine.PdfAConformance.PdfA1B;
            var page = document.AddPage();
            using var gfx = PdfGraphics.FromPdfPage(page);
            var content = NewTile(document, 50, new SolidBrush(PaintColor.FromArgb(255, 0, 0)));
            var mask = NewTile(document, 50, new SolidBrush(PaintColor.White));

            Assert.Throws<PdfAConformanceException>(() =>
                gfx.DrawImageAlphaMasked(content, mask, new Rect(10, 10, 50, 50)));
        }

        [Fact]
        public void DrawImageBlendedOver_NormalBlendMode_StillPaintsBothForms()
        {
            var (document, gfx) = NewPage();
            var top = NewTile(document, 50, new SolidBrush(PaintColor.FromArgb(255, 0, 0)));
            var bottom = NewTile(document, 50, new SolidBrush(PaintColor.FromArgb(0, 0, 255)));

            gfx.DrawImageBlendedOver(top, bottom, new Rect(10, 10, 50, 50), "Normal");
            gfx.Dispose();

            var text = Serialize(document);
            // "Do" alone as a substring also matches inside unrelated text (e.g. the PDF producer
            // comment's "PdfDocumentInformation") - anchor on the actual "/Fm<n> Do" placement operator.
            var doCount = Regex.Matches(text, @"/Fm\d+ Do").Count;
            Assert.Equal(2, doCount);
        }
    }
}
