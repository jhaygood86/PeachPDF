using PeachDrawing;
using PeachDrawing.Core;
using PeachDrawing.Text;
using PeachDrawing.Text.Shaping;
using PeachDrawing.Text.Unicode;
using PeachPDF.Tests.TestSupport;

namespace PeachDrawing.Tests
{
    /// <summary>
    /// A context whose layout unit is a CSS pixel (<see cref="RasterRenderContext.PixelsPerPoint"/> of 96/72) hands out fonts that report their metrics in
    /// layout units while drawing in points: the same font as one at one layout unit per point, scaled.
    /// </summary>
    public class RasterFontTests
    {
        private const double Scale = 96.0 / 72.0;

        private static async Task<(Font Plain, Font Scaled)> LoadAsync()
        {
            var plain = new RasterRenderContext();
            var scaled = new RasterRenderContext { PixelsPerPoint = Scale };
            var name = "RasterFont-" + Guid.NewGuid().ToString("N");
            foreach (var context in new[] { plain, scaled })
            {
                await using var stream = File.OpenRead(BundledFonts.Ttf);
                await context.AddFont(stream, name);
            }

            return (plain.GetFont(name, 16, PaintFontStyle.Regular)!, scaled.GetFont(name, 16 * Scale, PaintFontStyle.Regular)!);
        }

        [Fact]
        public async Task TheMetricsAreTheSameFontScaledToLayoutUnits()
        {
            var (plain, scaled) = await LoadAsync();

            Assert.Equal(plain.Height * Scale, scaled.Height, 3);
            Assert.Equal(Math.Round(plain.Ascent * Scale), scaled.Ascent, 3);
            Assert.Equal(plain.Ascent * Scale, scaled.TextBaselineOffset, 3);
            Assert.True(scaled.UnderlineThickness >= 1);
            Assert.True(scaled.UnderlineOffset > 0);
            Assert.Equal(scaled.Height / 6, scaled.LeftPadding, 6);
            Assert.True(scaled.NormalLineHeight > 0);
            _ = scaled.UnderlinePosition;
            Assert.Equal(plain.Typeface, scaled.Typeface);
            Assert.Equal(plain.Size * 1, scaled.Size, 6);
            Assert.Equal(plain.SyntheticStyle, scaled.SyntheticStyle);
            Assert.Equal(plain.ObliqueSkewSinus, scaled.ObliqueSkewSinus);
        }

        [Fact]
        public async Task TheGlyphAndFeatureQueriesAnswerForTheTypeface()
        {
            var (plain, scaled) = await LoadAsync();
            var a = new System.Text.Rune('A');

            Assert.True(scaled.HasGlyph(a));
            Assert.Equal(plain.GetGlyphIndex(a), scaled.GetGlyphIndex(a));
            Assert.Equal(plain.GetGlyphAdvanceWidthDesignUnits(plain.GetGlyphIndex(a)), scaled.GetGlyphAdvanceWidthDesignUnits(scaled.GetGlyphIndex(a)));
            Assert.Equal(plain.FontUnitsPerEm, scaled.FontUnitsPerEm);
            Assert.Equal(plain.XHeightEm, scaled.XHeightEm);
            Assert.Equal(plain.CapHeightEm, scaled.CapHeightEm);
            Assert.Equal(plain.GetBaselineHeightEm("ideo", false), scaled.GetBaselineHeightEm("ideo", false));
            Assert.Equal(plain.SupportsFontVariantCaps(CapsMode.SmallCaps), scaled.SupportsFontVariantCaps(CapsMode.SmallCaps));
            Assert.Equal(plain.SupportsFontVariantPosition(SubSuperMode.Super), scaled.SupportsFontVariantPosition(SubSuperMode.Super));
            Assert.Equal(plain.GetSubSuperscriptMetrics(true), scaled.GetSubSuperscriptMetrics(true));
            Assert.Equal(plain.GetSubSuperscriptMetrics(false), scaled.GetSubSuperscriptMetrics(false));
            Assert.Equal(plain.MatchesEmojiPresentation(a, EmojiPresentation.Text), scaled.MatchesEmojiPresentation(a, EmojiPresentation.Text));
            Assert.Equal(plain.HasMathTable, scaled.HasMathTable);
            Assert.Equal(plain.MathTable is null, scaled.MathTable is null);
            Assert.False(string.IsNullOrEmpty(scaled.FaceKey));
            Assert.Equal(scaled.FaceKey, scaled.FaceKey);
        }

        [Fact]
        public async Task TheVerticalMetricsAreScaledToLayoutUnits()
        {
            var (plain, scaled) = await LoadAsync();
            var a = new System.Text.Rune('A');

            Assert.Equal(plain.HasVerticalMetrics, scaled.HasVerticalMetrics);
            Assert.Equal(plain.HasVerticalOrigin, scaled.HasVerticalOrigin);
            Assert.Equal(plain.GetVerticalAdvance(a) * Scale, scaled.GetVerticalAdvance(a), 3);
            Assert.Equal(plain.GetVerticalOriginY(a) * Scale, scaled.GetVerticalOriginY(a), 3);
        }

        [Fact]
        public async Task ThePaletteQueriesAnswerForAFontWithoutColorGlyphs()
        {
            var (_, scaled) = await LoadAsync();

            Assert.Equal(0, scaled.PaletteCount);
            Assert.Equal(0, scaled.PaletteEntryCount);
            Assert.Null(scaled.FirstLightPalette());
            Assert.Null(scaled.FirstDarkPalette());
            Assert.False(scaled.TryGetPaletteColor(0, 0, out _));
        }

        [Fact]
        public async Task ThePaletteQueriesAnswerForAColorFont()
        {
            var context = new RasterRenderContext { PixelsPerPoint = Scale };
            await using var stream = File.OpenRead(BundledFonts.ColorV0);
            await context.AddFont(stream, "RasterColor");
            var font = context.GetFont("RasterColor", 20, PaintFontStyle.Regular)!;

            Assert.True(font.PaletteCount > 0);
            Assert.True(font.PaletteEntryCount > 0);
            Assert.True(font.TryGetPaletteColor(0, 0, out var color));
            Assert.NotEqual(PaintColor.Empty, color);
        }

        [Fact]
        public async Task TheWhitespaceWidthIsMeasuredOnceOnTheCanvas()
        {
            var (_, scaled) = await LoadAsync();
            var canvas = new RasterRenderContext { PixelsPerPoint = Scale }.CreateCanvas(10, 10);

            var first = scaled.GetWhitespaceWidth(canvas);

            Assert.True(first > 0);
            Assert.Equal(first, scaled.GetWhitespaceWidth(canvas));
        }
    }
}
