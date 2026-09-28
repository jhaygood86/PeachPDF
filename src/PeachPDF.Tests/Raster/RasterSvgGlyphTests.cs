using PeachPDF.Adapters;
using PeachDrawing.Abstractions;
using PeachPDF.PdfSharpCore;
using PeachDrawing;
using PeachPDF.Tests.TestSupport;
using System.Numerics;
using System.Text.RegularExpressions;

namespace PeachPDF.Tests.Raster
{
    /// <summary>
    /// A font's SVG glyph (OpenType SVG) drawn through <see cref="RasterCanvas"/>: the glyph's own document, in its own colours, rather than the
    /// plain outline in the text colour it used to be there. Every assertion here would fail for the outline: it is one colour, the text's.
    /// </summary>
    public class RasterSvgGlyphTests
    {
        private const string Family = "RasterSvgTestFace";

        private static async Task<(RasterCanvas Graphics, Font Font, PdfSharpAdapter Adapter)> NewFixture(int width = 160, int height = 120)
        {
            var adapter = new PdfSharpAdapter();
            await BundledFonts.RegisterFont(adapter, BundledFonts.SvgTest, Family);
            var graphics = new RasterCanvas(adapter, new RasterSurface(width, height, 0, 0, 1, 1), 1);
            return (graphics, adapter.GetFont(Family, 60, PaintFontStyle.Regular)!, adapter);
        }

        private static void Draw(RasterCanvas g, Font font, string text, PaintColor colour, FontPalette? palette = null) =>
            g.DrawString(text, font, colour, new PaintPoint(10, 10), g.MeasureString(text, font), 0, palette);

        private static List<(int X, int Y, byte R, byte G, byte B, byte A)> Pixels(RasterCanvas g)
        {
            var pixels = new List<(int X, int Y, byte R, byte G, byte B, byte A)>();
            for (var y = 0; y < g.Surface.Height; y++)
            {
                var row = g.Surface.Row(y);
                for (var x = 0; x < g.Surface.Width; x++)
                {
                    var a = row[x * 4 + 3];
                    if (a == 0)
                        continue;

                    // the surface holds premultiplied pixels; the opaque ones are what the assertions look at
                    pixels.Add((x, y, row[x * 4], row[x * 4 + 1], row[x * 4 + 2], a));
                }
            }

            return pixels;
        }

        private static (double X, double Y) Centre(IEnumerable<(int X, int Y)> points)
        {
            var list = points.ToList();
            Assert.NotEmpty(list);
            return (list.Average(p => p.X), list.Average(p => p.Y));
        }

        [Fact]
        public async Task TheGlyphIsDrawnInItsDocumentsColours_NotTheTextColour()
        {
            var (g, font, _) = await NewFixture();

            Draw(g, font, "A", PaintColor.FromArgb(255, 0, 0, 0));

            var opaque = Pixels(g).Where(p => p.A == 255).ToList();
            var red = opaque.Where(p => p is { R: 255, G: 0, B: 0 }).ToList();
            var blue = opaque.Where(p => p is { R: 0, G: 0, B: 255 }).ToList();

            // The palette's first colour is the square and its second the disc in the middle of it; the outline would be one black square.
            Assert.True(red.Count > 500, $"{red.Count} red pixels");
            Assert.True(blue.Count > 200, $"{blue.Count} blue pixels");
            Assert.DoesNotContain(opaque, p => p is { R: 0, G: 0, B: 0 });

            var (redX, redY) = Centre(red.Select(p => (p.X, p.Y)));
            var (blueX, blueY) = Centre(blue.Select(p => (p.X, p.Y)));
            Assert.InRange(blueX, redX - 3, redX + 3);
            Assert.InRange(blueY, redY - 3, redY + 3);
        }

        [Fact]
        public async Task TheGlyphIsWhereThePdfPlacesIt_TheOriginOnTheBaseline()
        {
            var (g, font, _) = await NewFixture();
            var real = ((FontAdapter)font).Font;

            Draw(g, font, "A", PaintColor.FromArgb(255, 0, 0, 0));

            // The square runs from the baseline up 0.8 em; it sits from 0.1 em to 0.9 em of the advance.
            var baseline = 10 + real.GetHeight() * real.CellAscent / real.CellSpace;
            var red = Pixels(g).Where(p => p is { R: 255, G: 0, B: 0, A: 255 }).ToList();
            Assert.InRange(red.Max(p => p.Y) + 1, baseline - 1.5, baseline + 1.5);
            Assert.InRange(red.Min(p => p.Y), baseline - 0.8 * 60 - 1.5, baseline - 0.8 * 60 + 1.5);
            Assert.InRange(red.Min(p => p.X), 10 + 0.1 * 60 - 1.5, 10 + 0.1 * 60 + 1.5);
            Assert.InRange(red.Max(p => p.X) + 1, 10 + 0.9 * 60 - 1.5, 10 + 0.9 * 60 + 1.5);
        }

        [Fact]
        public async Task ContextFill_IsTheTextColour()
        {
            var (g, font, _) = await NewFixture();

            Draw(g, font, "B", PaintColor.FromArgb(255, 0, 128, 0));

            var opaque = Pixels(g).Where(p => p.A == 255).ToList();
            Assert.True(opaque.Count > 500);
            Assert.All(opaque, p => Assert.Equal((0, 128, 0), (p.R, p.G, p.B)));
        }

        [Fact]
        public async Task AUseInTheGlyph_GivesItsShapeADistinctFillAndStroke_AndTheTextIsTheContextOfTheRest()
        {
            var (g, font, _) = await NewFixture();

            Draw(g, font, "F", PaintColor.FromArgb(255, 128, 0, 128));

            var opaque = Pixels(g).Where(p => p.A == 255).ToList();
            var orange = opaque.Where(p => p is { R: 255, G: 136, B: 0 }).ToList();
            var blue = opaque.Where(p => p is { R: 0, G: 68, B: 204 }).ToList();
            var text = opaque.Where(p => p is { R: 128, G: 0, B: 128 }).ToList();

            // The <use> is the context of the square: its own orange fill and blue stroke, two paints, not the one the text has.
            Assert.True(orange.Count > 1000, $"{orange.Count} orange pixels");
            Assert.True(blue.Count > 200, $"{blue.Count} blue pixels");

            // The disc is outside the use: it takes the text's fill. The frame that asks for the text's stroke is not drawn (the text has none),
            // so nothing in the text colour lies outside the disc.
            Assert.True(text.Count > 300, $"{text.Count} text-coloured pixels");
            var (cx, cy) = Centre(text.Select(p => (p.X, p.Y)));
            Assert.All(text, p => Assert.True(Math.Sqrt((p.X - cx) * (p.X - cx) + (p.Y - cy) * (p.Y - cy)) < 15, $"({p.X}, {p.Y}) is outside the disc"));
        }

        [Fact]
        public async Task ArtworkBeyondTheEmBox_IsDrawn_NotClippedAtTheLeastCanvas()
        {
            var (g, font, _) = await NewFixture(320, 120);

            // origin at x = 120: the red block lies 1.2 to 1.6 ems left of it and the green one 2.2 to 2.8 ems right of it (60 px per em), both
            // outside the least canvas (one em left, two right), and the blue disc is in the middle
            g.DrawString("G", font, PaintColor.FromArgb(255, 0, 0, 0), new PaintPoint(120, 10), g.MeasureString("G", font), 0, null);

            var opaque = Pixels(g).Where(p => p.A == 255).ToList();
            var red = opaque.Where(p => p is { R: 204, G: 0, B: 0 }).ToList();
            var green = opaque.Where(p => p is { R: 0, G: 170, B: 0 }).ToList();
            Assert.Contains(opaque, p => p is { R: 0, G: 0, B: 204 });
            Assert.True(red.Count > 300, $"{red.Count} red pixels");
            Assert.True(green.Count > 400, $"{green.Count} green pixels");
            Assert.InRange(red.Min(p => p.X), 23, 25);
            Assert.InRange(red.Max(p => p.X), 46, 48);
            Assert.InRange(green.Min(p => p.X), 251, 253);
            Assert.InRange(green.Max(p => p.X), 286, 288);
        }

        [Fact]
        public async Task AnotherPalette_ChangesTheColours()
        {
            var (g, font, _) = await NewFixture();
            var palette = new FontPalette(1, []);

            Draw(g, font, "A", PaintColor.FromArgb(255, 0, 0, 0), palette);

            // palette 1 is green then yellow (0, 0.6, 0) and (0.9, 0.9, 0)
            Assert.Contains(Pixels(g), p => p is { R: 0, G: 153, B: 0, A: 255 });
            Assert.DoesNotContain(Pixels(g), p => p is { R: 255, G: 0, B: 0, A: 255 });
        }

        [Fact]
        public async Task APaletteOverride_ReplacesTheEntry()
        {
            var (g, font, _) = await NewFixture();
            var palette = new FontPalette(0, [new KeyValuePair<int, PaintColor>(0, PaintColor.FromArgb(255, 124, 58, 237))]);

            Draw(g, font, "A", PaintColor.FromArgb(255, 0, 0, 0), palette);

            Assert.Contains(Pixels(g), p => p is { R: 124, G: 58, B: 237, A: 255 });
            Assert.Contains(Pixels(g), p => p is { R: 0, G: 0, B: 255, A: 255 });
        }

        [Fact]
        public async Task AGlyphWithoutAUsableDocument_IsItsOutlineInTheTextColour()
        {
            var (g, font, _) = await NewFixture();

            // E's document inflates past the size limit, so the glyph is its plain square in the text colour.
            Draw(g, font, "E", PaintColor.FromArgb(255, 200, 0, 0));

            var opaque = Pixels(g).Where(p => p.A == 255).ToList();
            Assert.True(opaque.Count > 500);
            Assert.All(opaque, p => Assert.Equal((200, 0, 0), (p.R, p.G, p.B)));
        }

        [Fact]
        public async Task AClipAroundTheRun_ClipsTheGlyph()
        {
            var (g, font, _) = await NewFixture();

            g.PushClip(new Rect(0, 0, 40, 120));
            Draw(g, font, "A", PaintColor.FromArgb(255, 0, 0, 0));
            g.PopClip();

            var pixels = Pixels(g).ToList();
            Assert.NotEmpty(pixels);
            Assert.True(pixels.Max(p => p.X) < 40);
        }

        [Fact]
        public async Task ATransformedRun_DrawsTheGlyphScaled()
        {
            var (g, font, _) = await NewFixture(320, 240);

            g.PushTransform(new Matrix3x2(2, 0, 0, 2, 0, 0));
            Draw(g, font, "A", PaintColor.FromArgb(255, 0, 0, 0));
            g.PopTransform();

            var red = Pixels(g).Where(p => p is { R: 255, G: 0, B: 0, A: 255 }).ToList();
            Assert.InRange(red.Max(p => p.X) - red.Min(p => p.X) + 1, 2 * 0.8 * 60 - 3, 2 * 0.8 * 60 + 3);
        }

        [Fact]
        public async Task ARepeatedGlyphInAnotherRegion_IsDrawnTheSame()
        {
            var (g, font, adapter) = await NewFixture();
            Draw(g, font, "A", PaintColor.FromArgb(255, 0, 0, 0));

            // a second surface on the same adapter reuses the glyph's document, and draws the same pixels
            var second = new RasterCanvas(adapter, new RasterSurface(160, 120, 0, 0, 1, 1), 1);
            Draw(second, font, "A", PaintColor.FromArgb(255, 0, 0, 0));

            Assert.Equal(Pixels(g), Pixels(second));
        }

        [Fact]
        public async Task AFilteredElement_ContainsTheRealGlyph_InTheEmbeddedBitmap()
        {
            var b64 = Convert.ToBase64String(File.ReadAllBytes(BundledFonts.SvgTest));
            var html = $@"<!DOCTYPE html><html><head><style>
@font-face {{ font-family: 'SvgTest'; src: url('data:font/truetype;base64,{b64}') format('truetype'); }}
body {{ margin: 0 }}
p {{ margin: 0; font-family: 'SvgTest'; font-size: 40pt; color: #000; filter: drop-shadow(3px 3px 2px #888) }}
</style></head><body><p>A</p></body></html>";

            var pdf = await PdfObjectReader.GeneratePdf(html, new PdfGenerateConfig
            {
                PageSize = PageSize.A4,
                CompressContentStreams = false,
                ImageCompression = ImageCompression.Lossless,
                RasterizationDpi = 150,
            });

            // The shadowed element is one bitmap: read its colour data and look for the glyph's own colours, which a text-colour outline never has.
            var image = Regex.Matches(pdf, @"(?:^|[\r\n])(\d+) 0 obj(.*?)endobj", RegexOptions.Singleline)
                .Select(m => (Number: int.Parse(m.Groups[1].Value), Text: m.Groups[2].Value))
                .First(o => o.Text.Contains("stream", StringComparison.Ordinal) &&
                            Regex.IsMatch(o.Text[..o.Text.IndexOf("stream", StringComparison.Ordinal)], @"/Subtype\s*/Image") &&
                            !o.Text.Contains("/ImageMask") && o.Text.Contains("/DeviceRGB"));
            var data = PdfObjectReader.Inflate(PdfObjectReader.StreamBytes(pdf, image.Number));

            var reds = 0;
            var blues = 0;
            for (var i = 0; i + 2 < data.Length; i += 3)
            {
                if (data[i] > 200 && data[i + 1] < 40 && data[i + 2] < 40)
                    reds++;
                else if (data[i] < 40 && data[i + 1] < 40 && data[i + 2] > 200)
                    blues++;
            }

            Assert.True(reds > 500, $"{reds} red pixels");
            Assert.True(blues > 200, $"{blues} blue pixels");
        }
    }
}
