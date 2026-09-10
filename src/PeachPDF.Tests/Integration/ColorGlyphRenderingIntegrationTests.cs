using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using PeachPDF;
using PeachPDF.Fonts;
using PeachPDF.PdfSharpCore;
using PeachPDF.PdfSharpCore.Drawing.Pdf;
using PeachPDF.Tests.TestSupport;
using PeachPDF.Text;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// End-to-end tests that a COLR/CPAL color font renders its glyphs as layered vector fills in
    /// the content stream (multiple solid fills per glyph, in the palette colors), with an invisible
    /// embedded-font text show solely for selection/search/copy geometry.
    /// </summary>
    public class ColorGlyphRenderingIntegrationTests
    {
        private static async Task<string> RenderWithColorFont(string fontPath, string body)
        {
            var family = TtfFontDescription.LoadDescription(fontPath).FontFamilyInvariantCulture;
            var generator = new PdfGenerator();
            await using (var stream = File.OpenRead(fontPath))
                await generator.AddFontFromStream(stream);

            var html = $"<!DOCTYPE html><html><head><style>" +
                       $"body {{ font-family: '{family}'; font-size: 100pt; color: black; }}" +
                       $"</style></head><body>{body}</body></html>";

            var config = new PdfGenerateConfig { PageSize = PageSize.A4, CompressContentStreams = false };
            var doc = await generator.GeneratePdf(html, config);
            var ms = new MemoryStream();
            doc.Save(ms);
            return Encoding.Latin1.GetString(ms.ToArray());
        }

        // All non-stroking RGB fill colors ("r g b rg") in document order.
        private static List<(double R, double G, double B)> FillColors(string pdf)
        {
            var colors = new List<(double, double, double)>();
            foreach (Match m in Regex.Matches(pdf, @"(-?\d*\.?\d+) (-?\d*\.?\d+) (-?\d*\.?\d+) rg"))
            {
                double D(int i) => double.Parse(m.Groups[i].Value, CultureInfo.InvariantCulture);
                colors.Add((D(1), D(2), D(3)));
            }
            return colors;
        }

        private static int Count(string haystack, string needle)
        {
            int n = 0, i = 0;
            while ((i = haystack.IndexOf(needle, i, StringComparison.Ordinal)) >= 0) { n++; i += needle.Length; }
            return n;
        }

        [Fact]
        public async Task ColrV0Glyph_PaintsLayeredSolidFills_AndInvisibleTextShow()
        {
            // 'A' is authored as a red box under a green triangle.
            string pdf = await RenderWithColorFont(BundledFonts.ColorV0, "A");

            // The CID text show is rendering mode 3 (invisible); vector fills remain the visible ink.
            Assert.Contains("3 Tr", pdf);
            Assert.Equal(1, Count(pdf, " Tj"));
            Assert.Equal(1, Count(pdf, "BT\n"));
            Assert.Equal(1, Count(pdf, "ET\n"));
            Assert.Contains("/FontFile2", pdf);

            var fills = FillColors(pdf);
            // Two layers -> two distinct solid fills.
            Assert.Contains(fills, c => Approx(c, 1, 0, 0));       // red box
            Assert.Contains(fills, c => Approx(c, 0, 0.5, 0));     // green triangle
            // At least two fill-path operators (one per layer).
            Assert.True(Count(pdf, "\nf\n") >= 2, "expected at least two fill operators for the two layers");
        }

        [Fact]
        public async Task PlainGlyphInColorFont_PaintsSingleForegroundFill()
        {
            // 'X' maps to the plain 'box' outline (no COLR record): drawn once in the text color.
            string pdf = await RenderWithColorFont(BundledFonts.ColorV0, "X");

            Assert.Equal(1, Count(pdf, " Tj"));
            var fills = FillColors(pdf);
            Assert.Contains(fills, c => Approx(c, 0, 0, 0)); // black text color
        }

        [Fact]
        public async Task ColrV1_LayeredSolidGlyph_PaintsPaletteFillsWithinGlyphClips()
        {
            // 'A' in the v1 font is PaintColrLayers of two PaintGlyph->PaintSolid layers.
            string pdf = await RenderWithColorFont(BundledFonts.ColorV1, "A");

            Assert.Equal(1, Count(pdf, " Tj"));
            var fills = FillColors(pdf);
            Assert.Contains(fills, c => Approx(c, 1, 0, 0));   // red box layer
            Assert.Contains(fills, c => Approx(c, 0, 0.5, 0)); // green triangle layer
            // Each PaintGlyph clips its fill to the glyph outline (W n).
            Assert.True(Count(pdf, "W n") >= 2, "each layer's PaintGlyph should establish a clip");
        }

        [Fact]
        public async Task ColrV1_LinearGradientGlyph_EmitsAxialShadingWithinGlyphClip()
        {
            // 'G' is PaintGlyph(box) -> PaintLinearGradient (red -> blue).
            string pdf = await RenderWithColorFont(BundledFonts.ColorV1, "G");

            Assert.Equal(1, Count(pdf, " Tj"));
            Assert.Contains("/ShadingType 2", pdf);            // axial (linear) gradient shading
            Assert.True(Count(pdf, "W n") >= 1, "the gradient is clipped to the glyph outline");
        }

        [Fact]
        public async Task ColrV1_CompositeGlyph_EmitsBlendModeExtGState()
        {
            // 'M' is PaintComposite(source=blue tri, MULTIPLY, backdrop=yellow box).
            string pdf = await RenderWithColorFont(BundledFonts.ColorV1, "M");

            Assert.Equal(1, Count(pdf, " Tj"));
            Assert.Contains("/BM", pdf);
            Assert.Contains("/Multiply", pdf);
        }

        [Fact]
        public async Task ColrV1_ReflectGradientGlyph_EmitsShadingWithExpandedStops()
        {
            // 'F' is a REFLECT-extend linear gradient; the stops are tiled/mirrored across periods.
            string pdf = await RenderWithColorFont(BundledFonts.ColorV1, "F");

            Assert.Equal(1, Count(pdf, " Tj"));
            Assert.Contains("/ShadingType 2", pdf);
            // The reflect expansion produces more than the two authored stops (a multi-bound stitching
            // function), unlike a plain 2-stop pad gradient.
            Assert.True(Count(pdf, "/FunctionType 2") >= 3, "reflect expansion should yield several stop segments");
        }

        [Fact]
        public async Task RealNotoColorEmoji_RendersAsVectorFillsWithGradients_AndInvisibleSelectableText()
        {
            // End-to-end against the real COLR v1 Noto Color Emoji subset: an emoji run must paint as
            // vector content (fills + gradient shadings), while invisible CID text supplies selection.
            string pdf = await RenderWithColorFont(BundledFonts.ColorEmoji, "\U0001F600\U0001F308"); // grin, rainbow

            Assert.Equal(2, Count(pdf, " Tj"));
            Assert.Contains("3 Tr", pdf);
            Assert.True(Count(pdf, "\nf\n") >= 1, "emoji should paint vector fills");
            Assert.Contains("/ShadingType", pdf);        // real Noto emoji use gradients
            Assert.Contains("/FontFile2", pdf);
            Assert.Contains("/ActualText <FEFFD83DDE00>", pdf); // grin
            Assert.Contains("/ActualText <FEFFD83CDF08>", pdf); // rainbow
        }

        [Fact]
        public async Task ColorEmojiSequence_CarriesItsWholeSourceAsActualText()
        {
            // Noto composes this six-UTF-16-unit sequence to one COLR glyph. Its vector paint must be
            // represented by one exact replacement string, including the otherwise-deleted VS16 and ZWJ.
            string pdf = await RenderWithColorFont(BundledFonts.ColorEmojiSequences,
                "\U0001F3F3\uFE0F\u200D\U0001F308"); // rainbow flag

            Assert.Equal(1, Count(pdf, " Tj"));
            Assert.Contains("3 Tr", pdf);
            Assert.Contains("/FontFile2", pdf);
            Assert.Contains("/ActualText <FEFFD83CDFF3FE0F200DD83CDF08>", pdf);
        }

        [Fact]
        public async Task SameGlyphWithAndWithoutTrailingVariationSelector_KeepsPerOccurrenceActualText()
        {
            // This font deliberately has no cmap glyph for VS16. Shape deletes its flagged .notdef at
            // the end, but the source code unit still belongs to the preceding visible heart for copy.
            // The two occurrences resolve to the same glyph ID, which is why a glyph-keyed /ToUnicode
            // map could not preserve both spellings while per-occurrence /ActualText can.
            string pdf = await RenderWithColorFont(BundledFonts.ColorEmojiSequences, "\u2764 \u2764\uFE0F");

            Assert.Equal(2, Count(pdf, " Tj"));
            Assert.Contains("/ActualText <FEFF2764>", pdf);
            Assert.Contains("/ActualText <FEFF2764FE0F>", pdf);
        }

        [Fact]
        public void ActualTextOwnership_UsesPositionallyAlignedLogicalTextForBidiTransformedRun()
        {
            // Mirrors CMapInfo.AddShapedText's contract. The displayed RTL run was reversed/mirrored
            // from source "(AB)" into "(BA)"; ReverseRunes(source) = ")BA(" is the logical source
            // aligned position-for-position with the displayed glyphs.
            ShapedGlyph[] glyphs =
            [
                new(1, 0, 1),
                new(2, 1, 1),
                new(3, 2, 1),
                new(4, 3, 1)
            ];

            string?[] actualText = ColorGlyphPainter.BuildActualTextByGlyph("(BA)", ")BA(", glyphs);

            Assert.Equal(new string?[] { ")", "B", "A", "(" }, actualText);
        }

        [Fact]
        public async Task ColrV1_RadialSweepAndTransformGlyphs_PaintAsVectorContent()
        {
            // R = radial, S = sweep, C/O/K/W/D/E/H/I/J = every transform variant, L = colr-glyph reference.
            string pdf = await RenderWithColorFont(BundledFonts.ColorV1, "RSCOKWLDEHIJ");

            Assert.Equal(12, Count(pdf, " Tj"));
            Assert.Contains("/ShadingType 3", pdf);   // radial gradient
            Assert.Contains("/ShadingType 4", pdf);   // sweep (conic) gradient mesh
            // Transform and colr-glyph paints still fill palette colors within glyph clips.
            var fills = FillColors(pdf);
            Assert.Contains(fills, c => Approx(c, 1, 0, 0)); // red (rotate over triangle, colrRef box)
            Assert.True(Count(pdf, "W n") >= 5, "each glyph paint clips to its outline");
        }

        [Fact]
        public async Task ColorGlyph_WithUnderline_StillDrawsDecoration()
        {
            // Exercises the shared underline/strikeout path on the color-font branch.
            var family = TtfFontDescription.LoadDescription(BundledFonts.ColorV1).FontFamilyInvariantCulture;
            var generator = new PdfGenerator();
            await using (var stream = File.OpenRead(BundledFonts.ColorV1))
                await generator.AddFontFromStream(stream);
            var html = $"<!DOCTYPE html><html><head><style>body {{ font-family: '{family}'; font-size: 60pt; " +
                       "text-decoration: underline line-through; }}</style></head><body>A</body></html>";
            var config = new PdfGenerateConfig { PageSize = PageSize.A4, CompressContentStreams = false };
            var doc = await generator.GeneratePdf(html, config);
            var ms = new MemoryStream();
            doc.Save(ms);
            var pdf = Encoding.Latin1.GetString(ms.ToArray());

            Assert.Equal(1, Count(pdf, " Tj"));
            // The color glyph still paints (its layers fill), and generating the PDF exercises the
            // shared underline/strikeout decoration path on the color-font branch without error.
            Assert.True(Count(pdf, "\nf\n") >= 2, "the color glyph's layers should fill");
            Assert.True(doc.PageCount >= 1);
        }

        [Fact]
        public async Task NonColorFont_StillUsesTextShow()
        {
            // Regression guard: an ordinary font keeps the embedded-font Tj path.
            var generator = new PdfGenerator();
            await using (var stream = File.OpenRead(BundledFonts.Ttf))
                await generator.AddFontFromStream(stream);
            var family = TtfFontDescription.LoadDescription(BundledFonts.Ttf).FontFamilyInvariantCulture;

            var html = $"<!DOCTYPE html><html><head><style>body {{ font-family: '{family}'; }}</style></head><body>Hi</body></html>";
            var config = new PdfGenerateConfig { PageSize = PageSize.A4, CompressContentStreams = false };
            var doc = await generator.GeneratePdf(html, config);
            var ms = new MemoryStream();
            doc.Save(ms);
            var pdf = Encoding.Latin1.GetString(ms.ToArray());

            Assert.True(Count(pdf, " Tj") >= 1, "ordinary text should still emit a Tj show");
            Assert.DoesNotContain("3 Tr", pdf);
            Assert.DoesNotContain("/ActualText", pdf);
        }

        private static bool Approx((double R, double G, double B) c, double r, double g, double b)
            => Math.Abs(c.R - r) < 0.02 && Math.Abs(c.G - g) < 0.02 && Math.Abs(c.B - b) < 0.02;
    }
}
