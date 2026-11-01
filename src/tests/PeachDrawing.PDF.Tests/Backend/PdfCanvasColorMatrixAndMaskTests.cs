using PeachDrawing.PDF;
using PeachDrawing.Core;
using PeachDrawing.PDF.Engine.Drawing;
using PeachDrawing.PDF.Engine;
using System.IO;
using System.Numerics;
using System.Text;

namespace PeachPDF.Tests.PdfBackendTests
{
    /// <summary>
    /// Exercises the new tile-compositing primitives through the actual production adapter
    /// (<see cref="PdfCanvas"/>) rather than the lower-level <c>PdfGraphics</c> calls
    /// <c>PdfGraphicsRendererColorMatrixAndMaskTests</c> drives directly - this is what proves the
    /// <c>Canvas</c>-to-<c>PdfGraphics</c> forwarding (including the <c>PaintBlendMode</c>-to-string
    /// crossing at this exact boundary - see <c>PdfGraphicsRenderer.DrawImageBlendedOver</c>'s remarks)
    /// actually works end to end, not just that the lower-level primitive does.
    /// </summary>
    public class PdfCanvasColorMatrixAndMaskTests
    {
        private static (PdfDocument Document, PdfGraphics PageGfx, PdfCanvas Graphics, PdfRenderContext RenderContext) NewPage()
        {
            var document = new PdfDocument();
            document.Options.CompressContentStreams = false;
            var page = document.AddPage();
            var pageGfx = PdfGraphics.FromPdfPage(page);
            var renderContext = new PdfRenderContext();
            var graphics = new PdfCanvas(renderContext, pageGfx, 1.0);
            return (document, pageGfx, graphics, renderContext);
        }

        private static Image NewTile(PdfCanvas graphics, RenderContext renderContext, double size, PaintColor color)
        {
            var (tileGraphics, image) = graphics.CreateTile(size, size)!.Value;
            tileGraphics.DrawRectangle(renderContext.GetSolidBrush(color), 0, 0, size, size);
            tileGraphics.Dispose();
            return image;
        }

        private static string Serialize(PdfDocument document)
        {
            var ms = new MemoryStream();
            document.Save(ms);
            return Encoding.Latin1.GetString(ms.ToArray());
        }

        [Fact]
        public void DrawImageWithOpacity_BlendModeParameter_ForwardsToBM()
        {
            var (document, pageGfx, graphics, renderContext) = NewPage();
            var tile = NewTile(graphics, renderContext, 50, PaintColor.FromArgb(255, 0, 0));

            graphics.DrawImageWithOpacity(tile, new Rect(10, 10, 50, 50), 1.0, PaintBlendMode.Multiply);
            pageGfx.Dispose();

            var text = Serialize(document);
            Assert.Contains("/BM /Multiply", text);
        }

        [Fact]
        public void DrawImageWithOpacity_DefaultBlendMode_OmitsBM()
        {
            var (document, pageGfx, graphics, renderContext) = NewPage();
            var tile = NewTile(graphics, renderContext, 50, PaintColor.FromArgb(255, 0, 0));

            graphics.DrawImageWithOpacity(tile, new Rect(10, 10, 50, 50), 0.5);
            pageGfx.Dispose();

            var text = Serialize(document);
            Assert.DoesNotContain("/BM", text);
            Assert.Contains("/ca 0.5", text);
        }

        [Fact]
        public void DrawImageWithColorMatrix_Forwards()
        {
            var (document, pageGfx, graphics, renderContext) = NewPage();
            var tile = NewTile(graphics, renderContext, 50, PaintColor.FromArgb(255, 0, 0));

            var brightness = new ColorMatrix(new Matrix4x4(
                1.5f, 0, 0, 0,
                0, 1.5f, 0, 0,
                0, 0, 1.5f, 0,
                0, 0, 0, 1), Vector4.Zero);

            graphics.DrawImageWithColorMatrix(tile, new Rect(10, 10, 50, 50), brightness);
            pageGfx.Dispose();

            var text = Serialize(document);
            Assert.Contains("/FunctionType 4", text);
        }

        [Fact]
        public void DrawImageAlphaMasked_Forwards()
        {
            var (document, pageGfx, graphics, renderContext) = NewPage();
            var content = NewTile(graphics, renderContext, 50, PaintColor.FromArgb(255, 0, 0));
            var mask = NewTile(graphics, renderContext, 50, PaintColor.FromArgb(255, 255, 255));

            graphics.DrawImageAlphaMasked(content, mask, new Rect(10, 10, 50, 50), invert: true);
            pageGfx.Dispose();

            var text = Serialize(document);
            Assert.Contains("/S /Alpha", text);
        }

        [Fact]
        public void DrawImageBlendedOver_Forwards()
        {
            var (document, pageGfx, graphics, renderContext) = NewPage();
            var top = NewTile(graphics, renderContext, 50, PaintColor.FromArgb(255, 0, 0));
            var bottom = NewTile(graphics, renderContext, 50, PaintColor.FromArgb(0, 0, 255));

            graphics.DrawImageBlendedOver(top, bottom, new Rect(10, 10, 50, 50), PaintBlendMode.Screen);
            pageGfx.Dispose();

            var text = Serialize(document);
            Assert.Contains("/BM /Screen", text);
        }

        [Fact]
        public void DrawImageWithColorMatrix_NonTileImage_IsNoOp()
        {
            // Contract shared with DrawImageMasked/DrawImageWithOpacity - a no-op if the image wasn't
            // created via CreateTile on this same Canvas (a raster PdfCanvasImage, not an PdfForm-backed one).
            var (document, pageGfx, graphics, renderContext) = NewPage();
            var pngBytes = PeachPDF.Tests.TestSupport.RasterPngFixture.MakeSolidRgbaPngBytes(2, 2, 255, 0, 0);
            var nonTileImage = renderContext.ImageFromStream(new MemoryStream(pngBytes));

            graphics.DrawImageWithColorMatrix(nonTileImage, new Rect(0, 0, 2, 2), ColorMatrix.Identity);
            pageGfx.Dispose();

            var text = Serialize(document);
            Assert.DoesNotContain("/FunctionType 4", text);
        }
    }
}
