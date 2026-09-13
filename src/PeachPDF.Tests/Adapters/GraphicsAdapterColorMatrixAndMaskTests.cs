using PeachPDF.Adapters;
using PeachPDF.Html.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.PdfSharpCore.Pdf;
using System.IO;
using System.Numerics;
using System.Text;

namespace PeachPDF.Tests.Adapters
{
    /// <summary>
    /// Exercises the new tile-compositing primitives through the actual production adapter
    /// (<see cref="GraphicsAdapter"/>) rather than the lower-level <c>XGraphics</c> calls
    /// <c>XGraphicsPdfRendererColorMatrixAndMaskTests</c> drives directly - this is what proves the
    /// <c>RGraphics</c>-to-<c>XGraphics</c> forwarding (including the <c>RBlendMode</c>-to-string
    /// crossing at this exact boundary - see <c>XGraphicsPdfRenderer.DrawImageBlendedOver</c>'s remarks)
    /// actually works end to end, not just that the lower-level primitive does.
    /// </summary>
    public class GraphicsAdapterColorMatrixAndMaskTests
    {
        private static (PdfDocument Document, XGraphics PageGfx, GraphicsAdapter Graphics, PdfSharpAdapter Adapter) NewPage()
        {
            var document = new PdfDocument();
            document.Options.CompressContentStreams = false;
            var page = document.AddPage();
            var pageGfx = XGraphics.FromPdfPage(page);
            var adapter = new PdfSharpAdapter();
            var graphics = new GraphicsAdapter(adapter, pageGfx, 1.0);
            return (document, pageGfx, graphics, adapter);
        }

        private static RImage NewTile(GraphicsAdapter graphics, RAdapter adapter, double size, RColor color)
        {
            var (tileGraphics, image) = graphics.CreateTile(size, size)!.Value;
            tileGraphics.DrawRectangle(adapter.GetSolidBrush(color), 0, 0, size, size);
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
            var (document, pageGfx, graphics, adapter) = NewPage();
            var tile = NewTile(graphics, adapter, 50, RColor.FromArgb(255, 0, 0));

            graphics.DrawImageWithOpacity(tile, new RRect(10, 10, 50, 50), 1.0, RBlendMode.Multiply);
            pageGfx.Dispose();

            var text = Serialize(document);
            Assert.Contains("/BM /Multiply", text);
        }

        [Fact]
        public void DrawImageWithOpacity_DefaultBlendMode_OmitsBM()
        {
            var (document, pageGfx, graphics, adapter) = NewPage();
            var tile = NewTile(graphics, adapter, 50, RColor.FromArgb(255, 0, 0));

            graphics.DrawImageWithOpacity(tile, new RRect(10, 10, 50, 50), 0.5);
            pageGfx.Dispose();

            var text = Serialize(document);
            Assert.DoesNotContain("/BM", text);
            Assert.Contains("/ca 0.5", text);
        }

        [Fact]
        public void DrawImageWithColorMatrix_Forwards()
        {
            var (document, pageGfx, graphics, adapter) = NewPage();
            var tile = NewTile(graphics, adapter, 50, RColor.FromArgb(255, 0, 0));

            var brightness = new ColorMatrix(new Matrix4x4(
                1.5f, 0, 0, 0,
                0, 1.5f, 0, 0,
                0, 0, 1.5f, 0,
                0, 0, 0, 1), Vector4.Zero);

            graphics.DrawImageWithColorMatrix(tile, new RRect(10, 10, 50, 50), brightness);
            pageGfx.Dispose();

            var text = Serialize(document);
            Assert.Contains("/FunctionType 4", text);
        }

        [Fact]
        public void DrawImageAlphaMasked_Forwards()
        {
            var (document, pageGfx, graphics, adapter) = NewPage();
            var content = NewTile(graphics, adapter, 50, RColor.FromArgb(255, 0, 0));
            var mask = NewTile(graphics, adapter, 50, RColor.FromArgb(255, 255, 255));

            graphics.DrawImageAlphaMasked(content, mask, new RRect(10, 10, 50, 50), invert: true);
            pageGfx.Dispose();

            var text = Serialize(document);
            Assert.Contains("/S /Alpha", text);
        }

        [Fact]
        public void DrawImageBlendedOver_Forwards()
        {
            var (document, pageGfx, graphics, adapter) = NewPage();
            var top = NewTile(graphics, adapter, 50, RColor.FromArgb(255, 0, 0));
            var bottom = NewTile(graphics, adapter, 50, RColor.FromArgb(0, 0, 255));

            graphics.DrawImageBlendedOver(top, bottom, new RRect(10, 10, 50, 50), RBlendMode.Screen);
            pageGfx.Dispose();

            var text = Serialize(document);
            Assert.Contains("/BM /Screen", text);
        }

        [Fact]
        public void DrawImageWithColorMatrix_NonTileImage_IsNoOp()
        {
            // Contract shared with DrawImageMasked/DrawImageWithOpacity - a no-op if the image wasn't
            // created via CreateTile on this same RGraphics (a raster ImageAdapter, not an XForm-backed one).
            var (document, pageGfx, graphics, adapter) = NewPage();
            var pngBytes = PeachPDF.Tests.TestSupport.RasterPngFixture.MakeSolidRgbaPngBytes(2, 2, 255, 0, 0);
            var nonTileImage = adapter.ImageFromStream(new MemoryStream(pngBytes));

            graphics.DrawImageWithColorMatrix(nonTileImage, new RRect(0, 0, 2, 2), ColorMatrix.Identity);
            pageGfx.Dispose();

            var text = Serialize(document);
            Assert.DoesNotContain("/FunctionType 4", text);
        }
    }
}
