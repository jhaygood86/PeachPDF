using PeachPDF.Adapters;
using PeachDrawing.Core;
using PeachDrawing;
using PeachPDF.Tests.TestSupport;
using System.Text.RegularExpressions;

namespace PeachPDF.Tests.Raster
{
    /// <summary>
    /// <see cref="PdfGenerateConfig.RasterAntiAliasing"/> reaches <see cref="ScanlineRasterizer"/> through
    /// <c>RenderContext.RasterAntiAliasing</c>: the same accumulated coverage is computed either way (see the
    /// rasterizer's own tests), only its final quantization to an alpha byte differs. These tests exercise
    /// that end to end through <see cref="RasterCanvas"/> - shape fills, clips and text alike - since that
    /// is the actual paint path a document's raster content goes through, not just the rasterizer in
    /// isolation.
    /// </summary>
    public class RasterAntiAliasingTests
    {
        private static RasterCanvas NewGraphics(bool antiAlias, int width = 20, int height = 20)
        {
            var adapter = new PdfSharpAdapter { RasterAntiAliasing = antiAlias };
            var surface = new RasterSurface(width, height, 0, 0, 1, 1);
            return new RasterCanvas(adapter, surface, 1);
        }

        private static byte AlphaAt(RasterCanvas g, int x, int y) => g.Surface.Row(y)[x * 4 + 3];

        private static Brush Solid(Canvas g, byte a, byte r, byte green, byte b) =>
            g.GetSolidBrush(PaintColor.FromArgb(a, r, green, b));

        private static bool HasFractionalAlpha(RasterCanvas g)
        {
            for (var y = 0; y < g.Surface.Height; y++)
            {
                var row = g.Surface.Row(y);
                for (var x = 0; x < g.Surface.Width; x++)
                {
                    var a = row[x * 4 + 3];
                    if (a != 0 && a != 255)
                        return true;
                }
            }

            return false;
        }

        // ---- shape fill --------------------------------------------------------------------------------------

        [Fact]
        public void ShapeFill_DefaultAntiAliasing_HasFractionalCoverageAtASubPixelEdge()
        {
            var g = NewGraphics(antiAlias: true);

            g.DrawRectangle(Solid(g, 255, 255, 0, 0), 2.5, 2, 4, 4);

            // Column 2 is half-covered by a rectangle starting at x = 2.5.
            Assert.InRange(AlphaAt(g, 2, 3), 100, 155);
        }

        [Fact]
        public void ShapeFill_AntiAliasingOff_HasOnlyHardEdges_NotANoOp()
        {
            var on = NewGraphics(antiAlias: true);
            var off = NewGraphics(antiAlias: false);

            on.DrawRectangle(Solid(on, 255, 255, 0, 0), 2.5, 2, 4, 4);
            off.DrawRectangle(Solid(off, 255, 255, 0, 0), 2.5, 2, 4, 4);

            // The default (on) produces a fractional edge; turning it off is not a no-op.
            Assert.InRange(AlphaAt(on, 2, 3), 100, 155);
            Assert.True(AlphaAt(off, 2, 3) is 0 or 255);
            Assert.NotEqual(AlphaAt(on, 2, 3), AlphaAt(off, 2, 3));

            // Every pixel this fill touches is fully transparent or fully opaque - never in between.
            Assert.False(HasFractionalAlpha(off));

            // Fully-inside and fully-outside pixels are unaffected by the toggle.
            Assert.Equal(255, AlphaAt(on, 4, 3));
            Assert.Equal(255, AlphaAt(off, 4, 3));
            Assert.Equal(0, AlphaAt(on, 0, 0));
            Assert.Equal(0, AlphaAt(off, 0, 0));
        }

        [Fact]
        public void PathClip_AntiAliasingOff_ThresholdsTheClipMaskEdgeToo()
        {
            // A diagonal clip path is where a clip mask (not a fill) carries anti-aliased coverage.
            var on = NewGraphics(antiAlias: true, width: 10, height: 10);
            var off = NewGraphics(antiAlias: false, width: 10, height: 10);

            void ClipAndFill(RasterCanvas g)
            {
                var path = g.GetGraphicsPath();
                path.Start(0, 0);
                path.LineTo(10, 0);
                path.LineTo(0, 10);
                path.CloseFigure();

                g.PushClip(path);
                g.DrawRectangle(Solid(g, 255, 0, 0, 255), 0, 0, 10, 10);
                g.PopClip();
            }

            ClipAndFill(on);
            ClipAndFill(off);

            // Along the diagonal, the default clip is fractional and the disabled one is hard 0/255.
            Assert.True(HasFractionalAlpha(on));
            Assert.False(HasFractionalAlpha(off));
        }

        // ---- text ---------------------------------------------------------------------------------------------

        [Fact]
        public async Task Text_AntiAliasingOff_HasOnlyHardEdges_NotANoOp()
        {
            const string family = "RasterAntiAliasingTestSans";

            var adapterOn = new PdfSharpAdapter { RasterAntiAliasing = true };
            await BundledFonts.RegisterFont(adapterOn, BundledFonts.Ttf, family);
            var adapterOff = new PdfSharpAdapter { RasterAntiAliasing = false };
            await BundledFonts.RegisterFont(adapterOff, BundledFonts.Ttf, family);

            var on = new RasterCanvas(adapterOn, new RasterSurface(200, 60, 0, 0, 1, 1), 1);
            var off = new RasterCanvas(adapterOff, new RasterSurface(200, 60, 0, 0, 1, 1), 1);

            var black = PaintColor.FromArgb(255, 0, 0, 0);
            var fontOn = adapterOn.GetFont(family, 28, PaintFontStyle.Regular)!;
            var fontOff = adapterOff.GetFont(family, 28, PaintFontStyle.Regular)!;

            on.DrawString("Sample", fontOn, black, new PaintPoint(10, 10), on.MeasureString("Sample", fontOn));
            off.DrawString("Sample", fontOff, black, new PaintPoint(10, 10), off.MeasureString("Sample", fontOff));

            // Small glyph outlines almost always leave a smoothed (fractional-alpha) edge when anti-aliased;
            // turning the setting off is not a no-op for text either.
            Assert.True(HasFractionalAlpha(on));
            Assert.False(HasFractionalAlpha(off));

            // Both still draw real ink (the toggle changes edge quantization, not whether anything is drawn).
            var inkOn = CountInk(on);
            var inkOff = CountInk(off);
            Assert.True(inkOn > 50);
            Assert.True(inkOff > 50);
        }

        // ---- end-to-end (full PDF generation) -----------------------------------------------------------------

        // A shape and some text, both forced into the raster path by a CSS filter (matching the SVG/paint
        // testing convention of verifying a whole-pipeline effect, not just the isolated primitive).
        private static string HtmlWithRasterShapeAndText =>
            "<html><head><style>" + BundledFonts.FontFaceRule(BundledFonts.Ttf, "RasterAaPdfTestSans", "font/truetype") +
            "</style></head><body style=\"margin:0;font:16px RasterAaPdfTestSans\">" +
            "<div style=\"filter:grayscale(1);width:120px\"><div style=\"width:60px;height:30px;background:#e33;border-radius:8px\"></div>Some text</div>" +
            "</body></html>";

        private static PdfGenerateConfig Config(bool? antiAlias) => new PdfGenerateConfig
        {
            PageSize = PageSize.A4,
            CompressContentStreams = false,
            RasterizationDpi = 96,
            MarginLeft = 0,
            MarginTop = 0,
            MarginRight = 0,
            MarginBottom = 0,
        }.With(antiAlias);

        private static string Scrub(string pdf)
        {
            pdf = Regex.Replace(pdf, @"/(CreationDate|ModDate)\s*\([^)]*\)", "/$1()");
            pdf = Regex.Replace(pdf, @"/ID\s*\[[^\]]*\]", "/ID[]");
            pdf = Regex.Replace(pdf, @"(?m)^%(?! ?PDF|%EOF)[^\r\n]*[\r\n]+", "");
            pdf = Regex.Replace(pdf, @"/[A-Z]{6}\+", "/XXXXXX+");
            return pdf;
        }

        private static async Task<string> Generate(bool? antiAlias) =>
            Scrub(await PdfObjectReader.GeneratePdf(HtmlWithRasterShapeAndText, Config(antiAlias)));

        [Fact]
        public void DefaultConfig_HasAntiAliasingOn()
        {
            Assert.True(new PdfGenerateConfig().RasterAntiAliasing);
        }

        [Fact]
        public async Task UnsetConfig_ProducesTheExactSameBytesAsExplicitlyTurningAntiAliasingOn()
        {
            // The default, byte-for-byte: this pins the guarantee that leaving RasterAntiAliasing untouched
            // never changes an existing document's output.
            Assert.Equal(await Generate(null), await Generate(true));
        }

        [Fact]
        public async Task TurningAntiAliasingOff_ActuallyChangesTheGeneratedPdf()
        {
            var on = await Generate(true);
            var off = await Generate(false);

            Assert.NotEqual(on, off);

            // Both still rasterize the region (this is a quantization change, not a fallback to vector content).
            Assert.Contains("/Subtype /Image", on.Replace("/Subtype/Image", "/Subtype /Image"));
            Assert.Contains("/Subtype /Image", off.Replace("/Subtype/Image", "/Subtype /Image"));
        }

        private static int CountInk(RasterCanvas g)
        {
            var ink = 0;
            for (var y = 0; y < g.Surface.Height; y++)
            {
                var row = g.Surface.Row(y);
                for (var x = 0; x < g.Surface.Width; x++)
                {
                    if (row[x * 4 + 3] > 0)
                        ink++;
                }
            }

            return ink;
        }
    }

    internal static class PdfGenerateConfigAntiAliasingTestExtensions
    {
        public static PdfGenerateConfig With(this PdfGenerateConfig config, bool? antiAlias)
        {
            if (antiAlias is { } value)
                config.RasterAntiAliasing = value;
            return config;
        }
    }
}
