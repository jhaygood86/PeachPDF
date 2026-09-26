using PeachDrawing.Text.Shaping;
using PeachPDF.Adapters;
using PeachPDF.Html.Adapters;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.Tests.TestSupport;
using PeachDrawing.Text.Internal.Text;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Regression coverage for CSS Fonts 4 <c>font-variant-position</c>. Two paths have to be pinned
    /// separately, because they look identical from the CSS side and completely different underneath:
    /// real <c>sups</c>/<c>subs</c> GSUB substitution on a font that has them (Source Sans 3, confirmed
    /// by direct byte inspection), and the synthesized fallback the spec requires on a font that does
    /// not (STIX Two Math, likewise confirmed - 28 GSUB features, none of them <c>sups</c>/<c>subs</c>).
    /// </summary>
    public class FontVariantPositionIntegrationTests
    {
        // Source Sans 3: has real sups/subs GSUB data.
        private static readonly string SupsFontBase64 = Convert.ToBase64String(File.ReadAllBytes(BundledFonts.Ttf));

        // STIX Two Math: a real production font with GSUB, but no sups/subs - so it takes the
        // synthesized path. Its OS/2 states ySuperscriptYSize 500 and ySuperscriptYOffset 500 against a
        // 1000 unitsPerEm, i.e. a half-size glyph raised half an em; the subscript pair is 500/250.
        private static readonly string NoSupsFontBase64 = Convert.ToBase64String(File.ReadAllBytes(BundledFonts.Math));

        private const double NoSupsSizeScale = 0.5;
        private const double NoSupsSuperOffsetEm = 0.5;
        private const double NoSupsSubOffsetEm = 0.25;

        [Fact]
        public async Task Super_ReachesTheBoxAsANonInitialValue()
        {
            // Deliberately asserts "super", not "normal": a keyword-only property whose test uses its own
            // initial value passes by coincidence even when PropertyFactory drops the whole declaration
            // before it ever reaches CssBox.
            var box = await FindWordsBox(SupsFontBase64, "<b id=\"w\" style=\"font-variant-position:super\">42</b>");

            Assert.Equal("super", box.FontVariantPosition.ToString());
            Assert.Equal(SubSuperMode.Super, box.RequestedFontVariantPosition);
        }

        [Theory]
        [InlineData("font-variant: super", "super")]
        [InlineData("font-variant: sub", "sub")]
        public async Task FontVariantShorthand_SetsThePositionLonghand(string declaration, string expected)
        {
            var box = await FindWordsBox(SupsFontBase64, "<b id=\"w\" style=\"" + declaration + "\">42</b>");

            Assert.Equal(expected, box.FontVariantPosition.ToString());
        }

        [Fact]
        public async Task FontShorthand_ResetsThePositionLonghand()
        {
            // CSS Fonts 4 7.7 "Reset Implicitly": `font` resets every font-variant-* longhand, even
            // though none of them can be written inside it.
            var box = await FindWordsBox(SupsFontBase64,
                "<div style=\"font-variant-position:super\"><b id=\"w\" style=\"font:12pt SupsFont\">42</b></div>");

            Assert.Equal("normal", box.FontVariantPosition.ToString());
        }

        [Fact]
        public async Task Super_OnAFontWithSups_SubstitutesRealGlyphs()
        {
            // Glyph indices, not measured width: a well-designed font's superscript figures aren't
            // guaranteed to differ in advance width from the default ones.
            var box = await FindWordsBox(SupsFontBase64, "<b id=\"w\" style=\"font-variant-position:super\">42</b>");
            var descriptor = ((FontAdapter)box.ActualFont).Font.Descriptor;

            Assert.NotNull(descriptor);

            var plain = descriptor!.Shape("42", ShapeSettings.Default);
            var superscript = descriptor.Shape("42", box.ActualTextShapingFeatures);

            Assert.NotEqual(plain.Select(g => g.GlyphIndex), superscript.Select(g => g.GlyphIndex));
        }

        [Fact]
        public async Task Sub_OnAFontWithSubs_SubstitutesRealGlyphs()
        {
            var box = await FindWordsBox(SupsFontBase64, "<b id=\"w\" style=\"font-variant-position:sub\">42</b>");
            var descriptor = ((FontAdapter)box.ActualFont).Font.Descriptor;

            Assert.NotNull(descriptor);

            var plain = descriptor!.Shape("42", ShapeSettings.Default);
            var subscript = descriptor.Shape("42", box.ActualTextShapingFeatures);

            Assert.NotEqual(plain.Select(g => g.GlyphIndex), subscript.Select(g => g.GlyphIndex));
        }

        [Fact]
        public async Task Super_OnAFontWithSups_TakesNoSynthesis()
        {
            var box = await FindWordsBox(SupsFontBase64, "<b id=\"w\" style=\"font-variant-position:super\">42</b>");

            // Real substitution is doing the work, so the word is left whole at full size and no baseline
            // shift is applied - requesting both would shrink glyphs that are already superscripts.
            Assert.Equal(SubSuperMode.Super, box.ActualFontVariantPosition);
            Assert.Null(box.SubSuperscriptSynthesis);
            Assert.Equal(1.0, Assert.Single(box.Words).FontSizeScale);
            Assert.Equal(ScaledFontKind.None, box.Words[0].ScaledFontKind);
        }

        [Fact]
        public async Task Super_OnAFontWithoutSups_SynthesizesFromThatFontsOwnOs2Metrics()
        {
            var box = await FindWordsBox(NoSupsFontBase64, "<b id=\"w\" style=\"font-variant-position:super\">42</b>");

            // No real feature to request, so the shaping layer is told nothing and the run is synthesized.
            Assert.Equal(SubSuperMode.None, box.ActualFontVariantPosition);
            Assert.Equal(SubSuperMode.Super, box.RequestedFontVariantPosition);

            var synthesis = box.SubSuperscriptSynthesis;
            Assert.NotNull(synthesis);
            Assert.Equal(NoSupsSizeScale, synthesis!.Value.SizeScale, 3);
            // Negative: a superscript sits above the baseline, and the layout axis grows downward.
            Assert.Equal(-NoSupsSuperOffsetEm * box.ActualFont.Size, synthesis.Value.BaselineShift, 3);
        }

        [Fact]
        public async Task Sub_OnAFontWithoutSubs_ShiftsTheBaselineTheOtherWay()
        {
            var box = await FindWordsBox(NoSupsFontBase64, "<b id=\"w\" style=\"font-variant-position:sub\">42</b>");

            var synthesis = box.SubSuperscriptSynthesis;
            Assert.NotNull(synthesis);
            Assert.Equal(NoSupsSubOffsetEm * box.ActualFont.Size, synthesis!.Value.BaselineShift, 3);
            Assert.True(synthesis.Value.BaselineShift > 0);
        }

        [Fact]
        public async Task SynthesizedSuperscript_KeepsTheWordWholeAndPicksTheScaledFace()
        {
            var box = await FindWordsBox(NoSupsFontBase64, "<b id=\"w\" style=\"font-variant-position:super\">42</b>");

            // Unlike small-caps synthesis there is no case-flip to split on, so the whole word takes one
            // uniform scale.
            var word = Assert.Single(box.Words);
            Assert.Equal("42", word.Text);
            Assert.Equal(NoSupsSizeScale, word.FontSizeScale, 3);
            Assert.Equal(ScaledFontKind.SubSuperscript, word.ScaledFontKind);

            // The discriminator, not the scale, is what picks the face - measurement and paint share this
            // one resolver, so disagreeing here shows up as a mis-aligned baseline rather than a failure.
            var resolved = CssBox.ResolveWordFont(word, box);
            Assert.Equal(box.ActualSubSuperscriptFont.Size, resolved.Size, 3);
            Assert.True(resolved.Size < box.ActualFont.Size);
        }

        [Fact]
        public async Task SynthesizedSuperscript_DoesNotChangeTheLineBoxHeight()
        {
            // CSS Fonts 4: the sub/superscript glyphs "have no effect on line-height and other box
            // characteristics" - so the synthesized, smaller face must not shrink the line either.
            var plain = await FindWordsBox(NoSupsFontBase64, "<b id=\"w\">42</b>");
            var superscript = await FindWordsBox(NoSupsFontBase64, "<b id=\"w\" style=\"font-variant-position:super\">42</b>");

            Assert.Equal(plain.Words[0].Height, superscript.Words[0].Height, 3);
        }

        [Fact]
        public async Task Normal_IsANoOpOnEitherFont()
        {
            foreach (var font in new[] { SupsFontBase64, NoSupsFontBase64 })
            {
                var box = await FindWordsBox(font, "<b id=\"w\" style=\"font-variant-position:normal\">42</b>");

                Assert.Equal(SubSuperMode.None, box.RequestedFontVariantPosition);
                Assert.Null(box.SubSuperscriptSynthesis);
                Assert.Equal(1.0, Assert.Single(box.Words).FontSizeScale);
            }
        }

        [Fact]
        public async Task Supports_FontVariantPositionSuper_IsHonoredAsARealCondition()
        {
            // The @supports oracle is the generated property registry, so registering the property is what
            // makes this condition true - and the UA stylesheet's own footnote-call rule depends on it.
            var box = await FindWordsBox(SupsFontBase64,
                "<style>@supports (font-variant-position: super) { #w { color: rgb(1, 2, 3); } }</style>" +
                "<b id=\"w\">42</b>");

            Assert.Equal("rgb(1, 2, 3)", box.Color);
        }

        [Fact]
        public async Task SynthesizedSuperscriptAndSubscript_ArePaintedOffTheSharedBaseline()
        {
            // The whole point of the feature is a shifted baseline, and a layout-only assertion cannot
            // see it: the shift is applied at paint time precisely so it does not disturb the line box.
            var container = await LayoutAsync(NoSupsFontBase64,
                "<p id=\"p\">x<span id=\"sup\" style=\"font-variant-position:super\">S</span>" +
                "<span id=\"sub\" style=\"font-variant-position:sub\">B</span></p>");

            var recorder = new RecordingGraphics(new PdfSharpAdapter());
            FragmentPaintHarness.PaintPage(container, recorder);

            var baseline = recorder.DrawnStrings.Single(d => d.Text == "x").Y;
            var superscript = recorder.DrawnStrings.Single(d => d.Text == "S").Y;
            var subscript = recorder.DrawnStrings.Single(d => d.Text == "B").Y;

            // The layout axis grows downward, so a superscript is painted at a smaller Y than the
            // ordinary text sharing its line, and a subscript at a larger one.
            Assert.True(superscript < baseline, $"superscript Y {superscript} should be above baseline Y {baseline}");
            Assert.True(subscript > baseline, $"subscript Y {subscript} should be below baseline Y {baseline}");
        }

        [Fact]
        public async Task NoSynthesis_LeavesTheSubSuperscriptFontAsTheBoxsOwnFont()
        {
            var box = await FindWordsBox(SupsFontBase64, "<b id=\"w\">42</b>");

            Assert.Null(box.SubSuperscriptSynthesis);
            Assert.Equal(box.ActualFont.Size, box.ActualSubSuperscriptFont.Size, 3);
        }

        private static string Wrap(string fontBase64, string body) =>
            "<!DOCTYPE html><html><head><style>\n" +
            "@font-face { font-family: 'SupsFont'; src: url('data:font/truetype;base64," + fontBase64 + "') format('truetype'); }\n" +
            "body { font-family: 'SupsFont'; width: 400px; }\n" +
            "</style></head><body>" + body + "</body></html>";

        private static async Task<HtmlContainerInt> LayoutAsync(string fontBase64, string body)
        {
            var adapter = new PdfSharpAdapter();
            var container = new HtmlContainerInt(adapter);
            await container.SetHtml(Wrap(fontBase64, body), null);

            var size = new XSize(595, 842);
            container.PageSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);
            container.MaxSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);

            var measure = XGraphics.CreateMeasureContext(size, XGraphicsUnit.Point, XPageDirection.Downwards);
            using var graphics = new GraphicsAdapter(adapter, measure, 1.0);
            await container.PerformLayout(graphics);

            Assert.NotNull(container.Root);
            return container;
        }

        private static async Task<CssBox> FindWordsBox(string fontBase64, string body)
        {
            var container = await LayoutAsync(fontBase64, body);

            var element = FindById(container.Root!, "w");
            Assert.NotNull(element);
            if (element!.Words.Count > 0) return element;

            var wordsChild = element.Boxes.FirstOrDefault(b => b.Words.Count > 0);
            Assert.NotNull(wordsChild);
            return wordsChild!;
        }

        private static CssBox? FindById(CssBox box, string id)
        {
            var val = box.HtmlTag?.TryGetAttribute("id", "");
            if (val != null && val.Equals(id, StringComparison.OrdinalIgnoreCase))
                return box;
            foreach (var child in box.Boxes)
            {
                var found = FindById(child, id);
                if (found != null) return found;
            }
            return null;
        }
    }
}
