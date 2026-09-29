using PeachPDF.Adapters;
using PeachDrawing.Core;
using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.PdfSharpCore.Pdf;
using System;
using System.IO;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;

namespace PeachPDF.Tests.Adapters
{
    /// <summary>
    /// A <see cref="TileBrush"/> or <see cref="HatchBrush"/> painted through the production PDF adapter becomes a real tiling pattern:
    /// the tile is written once and the pattern repeats it. These read the PDF that comes out.
    /// </summary>
    public class GraphicsAdapterTileBrushTests
    {
        private const string Png4X4 =
            "iVBORw0KGgoAAAANSUhEUgAAAAQAAAAECAIAAAAmkwkpAAAAE0lEQVR4nGM8YWTEAANMcBZeDgA8MgE0GRiVCQAAAABJRU5ErkJggg==";

        private static (PdfDocument Document, XGraphics PageGfx, GraphicsAdapter Graphics, PdfSharpAdapter Adapter) NewPage()
        {
            var document = new PdfDocument();
            document.Options.CompressContentStreams = false;
            var page = document.AddPage();
            var pageGfx = XGraphics.FromPdfPage(page);
            var adapter = new PdfSharpAdapter();
            return (document, pageGfx, new GraphicsAdapter(adapter, pageGfx, 1.0), adapter);
        }

        private static Image NewTile(GraphicsAdapter graphics, RenderContext adapter, double size)
        {
            var (tileGraphics, image) = graphics.CreateTile(size, size)!.Value;
            tileGraphics.DrawRectangle(adapter.GetSolidBrush(PaintColor.FromArgb(200, 30, 30)), 0, 0, size / 2, size);
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
        public void FormTile_BecomesATilingPatternWhoseContentDrawsTheFormOnce()
        {
            var (document, pageGfx, graphics, adapter) = NewPage();
            var brush = new TileBrush(NewTile(graphics, adapter, 20), 20, 20);

            graphics.DrawRectangle(brush, 10, 10, 300, 200);
            pageGfx.Dispose();

            var text = Serialize(document);
            Assert.Contains("/PatternType 1", text);
            Assert.Contains("/PaintType 1", text);
            Assert.Contains("/XStep 20", text);
            Assert.Contains("/YStep 20", text);
            Assert.Contains("/BBox [0 0 20 20]", text);
            Assert.Contains("/Pattern cs", text);
            Assert.Matches(new Regex(@"/\w+ scn"), text);
        }

        [Fact]
        public void CellSize_IsWrittenInPoints_WhenThePageUsesAScale()
        {
            var document = new PdfDocument();
            document.Options.CompressContentStreams = false;
            var pageGfx = XGraphics.FromPdfPage(document.AddPage());
            var adapter = new PdfSharpAdapter();
            var graphics = new GraphicsAdapter(adapter, pageGfx, 2.0);
            var brush = new TileBrush(NewTile(graphics, adapter, 40), 40, 40);

            graphics.DrawRectangle(brush, 0, 0, 200, 100);
            pageGfx.Dispose();

            var text = Serialize(document);
            Assert.Contains("/XStep 20", text);
            Assert.Contains("/YStep 20", text);
        }

        [Fact]
        public void Transform_ReachesThePatternMatrix()
        {
            var (document, pageGfx, graphics, adapter) = NewPage();
            var rotated = new TileBrush(NewTile(graphics, adapter, 20), 20, 20, Matrix3x2.CreateRotation(MathF.PI / 6));

            graphics.DrawRectangle(rotated, 0, 0, 200, 100);
            pageGfx.Dispose();

            var m = Regex.Match(Serialize(document), @"/PatternType 1[\s\S]*?/Matrix \[([^\]]+)\]|/Matrix \[([^\]]+)\][\s\S]*?/PatternType 1");
            Assert.True(m.Success);
            var numbers = (m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value)
                .Split(' ', StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal(6, numbers.Length);
            Assert.True(Math.Abs(double.Parse(numbers[1], System.Globalization.CultureInfo.InvariantCulture)) > 0.1);
        }

        [Fact]
        public void HatchBrush_IsPaintedAsATilingPattern()
        {
            var (document, pageGfx, graphics, _) = NewPage();

            graphics.DrawRectangle(new HatchBrush(HatchStyle.DiagonalCross, PaintColor.Black, PaintColor.White, 8), 0, 0, 100, 100);
            pageGfx.Dispose();

            var text = Serialize(document);
            Assert.Contains("/PatternType 1", text);
            Assert.Contains("/XStep 8", text);
        }

        [Fact]
        public void PenStrokedWithATile_UsesTheStrokePatternOperators()
        {
            var (document, pageGfx, graphics, adapter) = NewPage();
            var pen = graphics.GetPen(new TileBrush(NewTile(graphics, adapter, 20), 20, 20));
            pen.Width = 12;

            graphics.DrawLine(pen, 10, 10, 200, 10);
            pageGfx.Dispose();

            var text = Serialize(document);
            Assert.Contains("/Pattern CS", text);
            Assert.Matches(new Regex(@"/\w+ SCN"), text);
        }

        [Fact]
        public void ImageTile_IsEmbeddedOnceInsideThePattern()
        {
            var (document, pageGfx, graphics, adapter) = NewPage();
            var image = adapter.ImageFromStream(new MemoryStream(Convert.FromBase64String(Png4X4)));

            graphics.DrawRectangle(new TileBrush(image, 12, 12), 0, 0, 200, 100);
            pageGfx.Dispose();

            var text = Serialize(document);
            Assert.Contains("/PatternType 1", text);
            Assert.Contains("/Subtype /Image", text);
            Assert.Single(Regex.Matches(text, "/Subtype /Image"));
        }

        [Theory]
        [InlineData(ImageSampling.Nearest, false)]
        [InlineData(ImageSampling.Pixelated, false)]
        [InlineData(ImageSampling.Bilinear, true)]
        [InlineData(ImageSampling.Bicubic, true)]
        public void ImageTile_SamplingDecidesWhetherViewersMaySmoothIt(ImageSampling sampling, bool smooth)
        {
            var (document, pageGfx, graphics, adapter) = NewPage();
            var image = adapter.ImageFromStream(new MemoryStream(Convert.FromBase64String(Png4X4)));

            graphics.DrawRectangle(new TileBrush(image, 12, 12, Matrix3x2.Identity, sampling), 0, 0, 200, 100);
            pageGfx.Dispose();

            Assert.Equal(smooth, Serialize(document).Contains("/Interpolate true"));
        }

        [Fact]
        public void ATileTheAdapterDidNotMake_IsRejected()
        {
            var (_, pageGfx, graphics, _) = NewPage();
            using (pageGfx)
            {
                var foreign = new ForeignImage();

                Assert.Throws<NotSupportedException>(() => graphics.DrawRectangle(new TileBrush(foreign, 10, 10), 0, 0, 20, 20));
            }
        }

        [Fact]
        public void TileBrush_KeepsWhatItWasGiven()
        {
            var (_, pageGfx, graphics, adapter) = NewPage();
            using (pageGfx)
            {
                var tile = NewTile(graphics, adapter, 20);
                var brush = new TileBrush(tile, 15, 25, Matrix3x2.CreateTranslation(3, 4), ImageSampling.Bicubic);

                Assert.Same(tile, brush.Tile);
                Assert.Equal(15, brush.CellWidth);
                Assert.Equal(25, brush.CellHeight);
                Assert.Equal(Matrix3x2.CreateTranslation(3, 4), brush.Transform);
                Assert.Equal(ImageSampling.Bicubic, brush.Sampling);
            }
        }

        private sealed class ForeignImage : Image
        {
            public override double Width => 1;
            public override double Height => 1;
            public override bool Interpolate { get; set; }
            public override PixelBuffer? GetPixels() => null;
            public override void Dispose() { }
        }
    }
}
