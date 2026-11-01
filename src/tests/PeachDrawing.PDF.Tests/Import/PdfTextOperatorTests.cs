using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;
using PeachDrawing;
using PeachDrawing.Core;
using PeachDrawing.PDF.Engine;
using PeachDrawing.PDF.Import;
using PeachPDF.Tests.TestSupport;

namespace PeachDrawing.PDF.Tests.Import
{
    /// <summary>
    /// The text operators (Tf, Td, TD, T*, Tm, Tj, TJ, ', ", Tc, Tw, Tz, TL, Ts, Tr) run through the interpreter against a real embedded font:
    /// each test measures where ink lands on a raster canvas, so an operator that did nothing, or moved text the wrong way, fails.
    /// </summary>
    public class PdfTextOperatorTests
    {
        /// <summary>A page written by the composer with some text, so its resources hold a real embedded Type0 font (/F0).</summary>
        private static async Task<(PdfSourceDocument Doc, PdfDictionary Resources, string Hex)> OpenAsync()
        {
            var context = new PdfRenderContext();
            var name = "Ops-" + Guid.NewGuid().ToString("N");
            await using (var stream = File.OpenRead(BundledFonts.Ttf))
                await context.AddFont(stream, name);
            var font = context.GetFont(name, 30, PaintFontStyle.Regular)!;
            using var composer = new PdfComposer(context, new PdfComposerOptions { CompressContentStreams = false });
            composer.AddPage(300, 100).Canvas.DrawString("HH", font, PaintColor.Black, new PaintPoint(10, 10), new Size(200, 50));
            using var ms = new MemoryStream();
            composer.Save(ms);

            var doc = PdfSourceDocument.Open(ms.ToArray());
            var resources = (PdfDictionary)doc.InheritedAttribute(doc.Pages[0], "Resources")!;
            var content = Encoding.Latin1.GetString(doc.GetPageContent(doc.Pages[0]));
            var hex = Regex.Match(content, "<([0-9A-Fa-f]{4})(?:[0-9A-Fa-f]{4})*> Tj").Groups[1].Value;
            Assert.NotEmpty(hex);
            return (doc, resources, hex);
        }

        /// <summary>Runs <paramref name="content"/> on a 300x100 raster canvas (PDF space, y up) and returns the ink's bounding box and the features reported.</summary>
        private static ((int X0, int Y0, int X1, int Y1)? Box, int Pixels, IReadOnlyCollection<string> Unsupported) Run(PdfSourceDocument doc, PdfDictionary resources, string content)
        {
            using var canvas = new RasterRenderContext().CreateCanvas(300, 100);
            canvas.PushTransform(new Matrix3x2(1, 0, 0, -1, 0, 100));
            var interpreter = new PdfContentInterpreter(doc, canvas);
            interpreter.Run(Encoding.Latin1.GetBytes(content), resources);
            return (Measure(canvas, out var pixels), pixels, interpreter.Unsupported);
        }

        private static (int X0, int Y0, int X1, int Y1)? Measure(RasterCanvas canvas, out int pixels)
        {
            var buffer = canvas.ToPixelBuffer();
            var span = buffer.PremultipliedRgba.Span;
            int x0 = int.MaxValue, y0 = int.MaxValue, x1 = -1, y1 = -1;
            pixels = 0;
            for (var y = 0; y < buffer.Height; y++)
                for (var x = 0; x < buffer.Width; x++)
                    if (span[(y * buffer.Width + x) * 4 + 3] > 40)
                    {
                        pixels++;
                        (x0, y0, x1, y1) = (Math.Min(x0, x), Math.Min(y0, y), Math.Max(x1, x), Math.Max(y1, y));
                    }

            return pixels == 0 ? null : (x0, y0, x1, y1);
        }

        private static (int Red, int Blue) CountColours(RasterCanvas canvas)
        {
            var span = canvas.ToPixelBuffer().PremultipliedRgba.Span;
            int red = 0, blue = 0;
            for (var i = 0; i < span.Length; i += 4)
            {
                if (span[i + 3] < 200) continue;
                if (span[i] > 200 && span[i + 2] < 60) red++;
                if (span[i + 2] > 200 && span[i] < 60) blue++;
            }

            return (red, blue);
        }

        [Fact]
        public async Task Tj_DrawsTheGlyphAtTheTextPosition()
        {
            var (doc, res, hex) = await OpenAsync();

            var run = Run(doc, res, $"BT /F0 40 Tf 100 30 Td <{hex}> Tj ET");

            Assert.Empty(run.Unsupported);
            Assert.NotNull(run.Box);
            Assert.InRange(run.Box!.Value.X0, 98, 125);              // starts near x = 100
            Assert.InRange(run.Box.Value.Y1, 55, 75);                // its baseline is 30pt up from the bottom: y(canvas) = 70
        }

        [Fact]
        public async Task TJ_AdjustmentsMoveLaterGlyphs_AndPositiveAdjustmentsPullThemLeft()
        {
            var (doc, res, hex) = await OpenAsync();

            var plain = Run(doc, res, $"BT /F0 30 Tf 10 30 Td [<{hex}> <{hex}>] TJ ET").Box!.Value;
            var spread = Run(doc, res, $"BT /F0 30 Tf 10 30 Td [<{hex}> -1000 <{hex}>] TJ ET").Box!.Value;
            var tight = Run(doc, res, $"BT /F0 30 Tf 10 30 Td [<{hex}> 500 <{hex}>] TJ ET").Box!.Value;

            Assert.True(spread.X1 - plain.X1 >= 25, "a -1000 adjustment moves the next glyph one em (30pt) right");
            Assert.True(plain.X1 - tight.X1 >= 10, "a +500 adjustment pulls the next glyph half an em left");
        }

        [Fact]
        public async Task Tc_Tz_Ts_Tm_ChangeSpacingWidthRiseAndPlacement()
        {
            var (doc, res, hex) = await OpenAsync();
            var pair = hex + hex;
            var baseline = Run(doc, res, $"BT /F0 30 Tf 10 30 Td <{pair}> Tj ET").Box!.Value;

            var spaced = Run(doc, res, $"BT /F0 30 Tf 10 30 Td 20 Tc <{pair}> Tj ET").Box!.Value;
            var narrow = Run(doc, res, $"BT /F0 30 Tf 50 Tz 10 30 Td <{pair}> Tj ET").Box!.Value;
            var raised = Run(doc, res, $"BT /F0 30 Tf 20 Ts 10 30 Td <{pair}> Tj ET").Box!.Value;
            var placed = Run(doc, res, $"BT /F0 30 Tf 1 0 0 1 150 30 Tm <{pair}> Tj ET").Box!.Value;

            Assert.True(spaced.X1 - baseline.X1 >= 15, "Tc adds space after each glyph");
            Assert.True(baseline.X1 - narrow.X1 >= 8, "Tz 50 halves the glyph widths");
            Assert.True(baseline.Y0 - raised.Y0 >= 15, "Ts lifts the baseline");
            Assert.True(placed.X0 - baseline.X0 >= 130, "Tm sets the text matrix");
        }

        [Fact]
        public async Task LineOperators_MoveDownByTheLeading()
        {
            var (doc, res, hex) = await OpenAsync();

            var one = Run(doc, res, $"BT /F0 20 Tf 10 80 Td <{hex}> Tj ET").Box!.Value;
            var star = Run(doc, res, $"BT /F0 20 Tf 25 TL 10 80 Td <{hex}> Tj T* <{hex}> Tj ET").Box!.Value;
            var quote = Run(doc, res, $"BT /F0 20 Tf 25 TL 10 80 Td <{hex}> Tj <{hex}> ' ET").Box!.Value;
            var dquote = Run(doc, res, $"BT /F0 20 Tf 25 TL 10 80 Td <{hex}> Tj 3 2 <{hex}> \" ET").Box!.Value;
            var td = Run(doc, res, $"BT /F0 20 Tf 10 80 Td <{hex}> Tj 0 -25 TD <{hex}> Tj T* <{hex}> Tj ET").Box!.Value;

            Assert.True(star.Y1 - one.Y1 >= 20, "T* starts a line one leading lower");
            Assert.True(quote.Y1 - one.Y1 >= 20, "' moves to the next line and shows the string");
            Assert.True(dquote.Y1 - one.Y1 >= 20, "\" sets the spacing, moves to the next line and shows the string");
            Assert.True(td.Y1 - one.Y1 >= 45, "TD moves and sets the leading, which T* then uses");
        }

        [Fact]
        public async Task InvisibleText_DrawsNothing_AndStrokedModesAreReported()
        {
            var (doc, res, hex) = await OpenAsync();

            var invisible = Run(doc, res, $"BT /F0 30 Tf 3 Tr 10 30 Td <{hex}> Tj ET");
            var stroked = Run(doc, res, $"BT /F0 30 Tf 1 Tr 10 30 Td <{hex}> Tj ET");

            Assert.Equal(0, invisible.Pixels);
            Assert.Empty(invisible.Unsupported);
            Assert.Empty(stroked.Unsupported);
            Assert.True(stroked.Pixels > 0, "a stroked glyph is drawn as an outline");
        }

        [Fact]
        public async Task MissingFont_ZeroSize_AndNegativeSize_AreHandled()
        {
            var (doc, res, hex) = await OpenAsync();

            var missing = Run(doc, res, $"BT /Nope 30 Tf 10 30 Td <{hex}> Tj ET");
            var zero = Run(doc, res, $"BT /F0 0 Tf 10 30 Td <{hex}> Tj ET");
            var negative = Run(doc, res, $"BT /F0 -30 Tf 100 30 Td <{hex}> Tj ET");

            Assert.Contains("Tf (missing font)", missing.Unsupported);
            Assert.Equal(0, missing.Pixels);
            Assert.Equal(0, zero.Pixels);
            Assert.True(negative.Pixels > 0);
        }

        [Fact]
        public async Task TextState_IsSavedByQ_AndColourComesFromTheFillColour()
        {
            var (doc, res, hex) = await OpenAsync();
            using var canvas = new RasterRenderContext().CreateCanvas(300, 100);
            canvas.PushTransform(new Matrix3x2(1, 0, 0, -1, 0, 100));
            var interpreter = new PdfContentInterpreter(doc, canvas);

            interpreter.Run(Encoding.Latin1.GetBytes($"1 0 0 rg BT /F0 40 Tf 10 30 Td <{hex}> Tj ET q 0 0 1 rg /F0 10 Tf Q BT 150 30 Td <{hex}> Tj ET"), res);

            var (red, blue) = CountColours(canvas);

            Assert.True(red > 100, "the text is drawn in the fill colour");
            Assert.Equal(0, blue);                                   // the blue was set inside q ... Q
            // The second glyph still has the 40pt size: Q restored it, so it is as tall as the first.
            Assert.True(Measure(canvas, out _)!.Value.Y1 - Measure(canvas, out _)!.Value.Y0 > 20);
        }

        private static (int Red, int Blue, int Black, int Ink) RunColours(PdfSourceDocument doc, PdfDictionary resources, string content)
        {
            using var canvas = new RasterRenderContext().CreateCanvas(300, 100);
            canvas.PushTransform(new Matrix3x2(1, 0, 0, -1, 0, 100));
            var interpreter = new PdfContentInterpreter(doc, canvas);
            interpreter.Run(Encoding.Latin1.GetBytes(content), resources);
            interpreter.Finish();
            var span = canvas.ToPixelBuffer().PremultipliedRgba.Span;
            int red = 0, blue = 0, black = 0, ink = 0;
            for (var i = 0; i < span.Length; i += 4)
            {
                if (span[i + 3] < 200) continue;
                ink++;
                if (span[i] > 200 && span[i + 1] < 60 && span[i + 2] < 60) red++;
                if (span[i + 2] > 200 && span[i] < 60 && span[i + 1] < 60) blue++;
                if (span[i] < 40 && span[i + 1] < 40 && span[i + 2] < 40) black++;
            }

            return (red, blue, black, ink);
        }

        [Fact]
        public async Task StrokedText_IsDrawnAsAnOutlineInTheStrokeColour_AndFilledTextAsAnArea()
        {
            var (doc, res, hex) = await OpenAsync();
            var shown = $"BT /F0 60 Tf 10 20 Td <{hex}> Tj ET";

            var stroked = RunColours(doc, res, $"1 0 0 RG 2 w 1 Tr {shown}");
            var both = RunColours(doc, res, $"1 0 0 RG 0 g 2 w 2 Tr {shown}");
            var filled = RunColours(doc, res, $"0 g {shown}");

            Assert.True(stroked.Red > 50, "the outline is in the stroke colour");
            Assert.Equal(0, stroked.Black);                                  // and nothing is filled
            Assert.True(both.Red > 50 && both.Black > 100, "fill and stroke together");
            Assert.True(filled.Black > both.Red, "a filled glyph covers far more than its outline");
        }

        [Fact]
        public async Task ClippingRenderModes_RestrictLaterPaintToTheGlyphs_UntilTheStateEnds()
        {
            var (doc, res, hex) = await OpenAsync();
            var shown = $"BT /F0 60 Tf 10 20 Td <{hex}> Tj ET";
            var wholePage = "0 0 1 rg 0 0 300 100 re f";

            var unclipped = RunColours(doc, res, wholePage);
            var clipped = RunColours(doc, res, $"q 7 Tr {shown} {wholePage} Q");
            var fillClipped = RunColours(doc, res, $"1 0 0 rg q 4 Tr {shown} 0 0 1 rg {wholePage} Q");
            var restored = RunColours(doc, res, $"q 7 Tr {shown} Q {wholePage}");

            Assert.Equal(300 * 100, unclipped.Blue);
            Assert.InRange(clipped.Blue, 100, 6000);                         // only inside the glyph
            Assert.True(fillClipped.Blue > 100 && fillClipped.Blue < 6000, "mode 4 clips too");
            Assert.Equal(300 * 100, restored.Blue);                          // Q undoes the clip
        }

        [Fact]
        public async Task StrokeAndClipTogether_AreBothApplied()
        {
            var (doc, res, hex) = await OpenAsync();

            var both = RunColours(doc, res, $"1 0 0 RG 2 w q BT /F0 60 Tf 5 Tr 10 20 Td <{hex}> Tj ET 0 0 1 rg 0 0 300 100 re f Q");

            Assert.True(both.Blue > 100 && both.Red > 20, "the glyph is clipped to and outlined");
        }
    }
}
