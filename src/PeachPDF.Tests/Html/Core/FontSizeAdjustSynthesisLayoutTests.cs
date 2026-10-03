using PeachDrawing.Core;
using PeachPDF.Adapters;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.Tests.TestSupport;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Html.Core
{
    /// <summary>
    /// <c>font-size-adjust</c> (CSS Fonts 5 §3.2) and <c>font-synthesis*</c> (CSS Fonts 4 §3.5) through real layout. Both are
    /// asserted on what they change - the size the glyph font is created at, the synthesis the font carries, the faces
    /// the cache hands out - so a no-op implementation fails.
    /// </summary>
    public class FontSizeAdjustSynthesisLayoutTests
    {
        private static CssBox? Find(CssBox box, string id)
        {
            if (box.HtmlTag?.TryGetAttribute("id") == id) return box;
            foreach (var child in box.Boxes)
            {
                var found = Find(child, id);
                if (found is not null) return found;
            }

            return null;
        }

        private sealed record Layout(PdfSharpAdapter Adapter, CssBox Root)
        {
            public CssBox Box(string id) => Find(Root, id) ?? throw new InvalidOperationException($"no box #{id}");
        }

        private static async Task<Layout> LayoutAsync(string css, string body)
        {
            var adapter = new PdfSharpAdapter();
            await BundledFonts.RegisterFont(adapter, BundledFonts.Ttf, "AdjustFam");
            await BundledFonts.RegisterFont(adapter, BundledFonts.Otf, "OtherFam");
            await BundledFonts.RegisterFont(adapter, BundledFonts.Math, "NoSupsFam");

            var container = new HtmlContainerInt(adapter);
            await container.SetHtml($"<html><head><style>body {{ margin: 0 }} {css}</style></head><body>{body}</body></html>", null);

            var size = new XSize(595, 842);
            container.PageSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);
            container.MaxSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);

            var measure = XGraphics.CreateMeasureContext(size, XGraphicsUnit.Point, XPageDirection.Downwards);
            using var graphics = new GraphicsAdapter(adapter, measure, 1.0);
            await container.PerformLayout(graphics);

            return new Layout(adapter, container.Root!);
        }

        // ---- font-size-adjust ----------------------------------------------------------------------

        [Fact]
        public async Task FontSizeAdjust_Number_CreatesTheGlyphFontAtSizeTimesAdjustOverRatio()
        {
            var layout = await LayoutAsync("div { font-family: AdjustFam; font-size: 20pt }",
                "<div id='plain'>x</div><div id='adj' style='font-size-adjust: 0.6'>x</div>");

            var plain = layout.Box("plain").ActualFont;
            var adjusted = layout.Box("adj").ActualFont;
            var xHeight = plain.XHeightEm!.Value;

            Assert.Equal(20, plain.Size, 3);
            Assert.Equal(20 * 0.6 / xHeight, adjusted.Size, 3);
            Assert.NotEqual(plain.Size, adjusted.Size);
        }

        [Fact]
        public async Task FontSizeAdjust_CapHeightKeyword_UsesTheCapHeightRatio()
        {
            var layout = await LayoutAsync("div { font-family: AdjustFam; font-size: 20pt }",
                "<div id='plain'>x</div><div id='adj' style='font-size-adjust: cap-height 0.9'>x</div>");

            var capHeight = layout.Box("plain").ActualFont.CapHeightEm!.Value;

            Assert.Equal(20 * 0.9 / capHeight, layout.Box("adj").ActualFont.Size, 3);
        }

        [Fact]
        public async Task FontSizeAdjust_FromFont_LeavesThePrimaryFaceAlone()
        {
            var layout = await LayoutAsync("div { font-family: AdjustFam; font-size: 20pt }",
                "<div id='adj' style='font-size-adjust: from-font'>x</div>");

            Assert.Equal(20, layout.Box("adj").ActualFont.Size, 3);
        }

        [Fact]
        public async Task FontSizeAdjust_None_IsTheInitialValue_AndInherits()
        {
            var layout = await LayoutAsync("div { font-family: AdjustFam; font-size: 20pt }",
                "<div id='outer' style='font-size-adjust: 0.6'><span id='inner'>x</span></div><div id='reset' style='font-size-adjust: 0.6'><span id='off' style='font-size-adjust: none'>x</span></div>");

            // The adjusted size is inherited as a *factor*: the child re-derives its own glyph size from its own face.
            Assert.Equal(layout.Box("outer").ActualFont.Size, layout.Box("inner").ActualFont.Size, 3);
            Assert.Equal(20, layout.Box("off").ActualFont.Size, 3);
        }

        [Fact]
        public async Task FontSizeAdjust_DoesNotChangeEmResolution()
        {
            var layout = await LayoutAsync("div { font-family: AdjustFam; font-size: 20pt }",
                "<div id='adj' style='font-size-adjust: 0.6'><span id='child' style='font-size: 2em'>x</span></div>");

            var adj = layout.Box("adj");
            var child = layout.Box("child");

            // 1em is still the computed font-size (20pt), and 2em the child's 40pt: only the glyph fonts carry the adjustment.
            Assert.Equal(20, adj.GetEmHeight(), 3);
            Assert.Equal(40, child.GetEmHeight(), 3);
            Assert.NotEqual(adj.ActualFont.Size, adj.GetEmHeight());

            // Both adjusted by the same factor, so their glyph sizes keep the 2:1 ratio the em arithmetic asked for.
            Assert.Equal(2.0, child.ActualFont.Size / adj.ActualFont.Size, 3);
        }

        [Fact]
        public async Task FontSizeAdjust_FallbackFace_IsAdjustedByItsOwnRatio()
        {
            // A per-codepoint font is created from the *unadjusted* size and then adjusted for its own face's ratio.
            var layout = await LayoutAsync("div { font-family: AdjustFam, OtherFam; font-size: 20pt }",
                "<div id='adj' style='font-size-adjust: 0.6'>x</div>");

            var box = layout.Box("adj");
            var fallback = box.DerivedStyle.ActualFontForCodepoint(new System.Text.Rune('x'));
            var ratio = fallback.XHeightEm!.Value;

            Assert.Equal(20 * 0.6 / ratio, fallback.Size, 3);
        }

        // ---- font-synthesis ------------------------------------------------------------------------

        [Fact]
        public async Task FontSynthesis_Auto_SynthesizesBoldAndItalic()
        {
            var layout = await LayoutAsync("div { font-family: AdjustFam; font-size: 20pt; font-weight: bold; font-style: italic }", "<div id='d'>x</div>");

            Assert.Equal(PeachDrawing.Text.SyntheticStyle.BoldItalic, layout.Box("d").ActualFont.SyntheticStyle);
        }

        [Theory]
        [InlineData("font-synthesis: none", PeachDrawing.Text.SyntheticStyle.None)]
        [InlineData("font-synthesis: weight", PeachDrawing.Text.SyntheticStyle.Bold)]
        [InlineData("font-synthesis: style", PeachDrawing.Text.SyntheticStyle.Italic)]
        [InlineData("font-synthesis: weight style", PeachDrawing.Text.SyntheticStyle.BoldItalic)]
        [InlineData("font-synthesis: style small-caps position", PeachDrawing.Text.SyntheticStyle.Italic)]
        [InlineData("font-synthesis-weight: none", PeachDrawing.Text.SyntheticStyle.Italic)]
        [InlineData("font-synthesis-style: none", PeachDrawing.Text.SyntheticStyle.Bold)]
        [InlineData("font-synthesis: none; font-synthesis-weight: auto", PeachDrawing.Text.SyntheticStyle.Bold)]
        public async Task FontSynthesis_RestrictsWhatTheFontFakes(string declaration, PeachDrawing.Text.SyntheticStyle expected)
        {
            var layout = await LayoutAsync($"div {{ font-family: AdjustFam; font-size: 20pt; font-weight: bold; font-style: italic; {declaration} }}", "<div id='d'>x</div>");

            Assert.Equal(expected, layout.Box("d").ActualFont.SyntheticStyle);
        }

        [Fact]
        public async Task FontSynthesis_Inherits()
        {
            var layout = await LayoutAsync("div { font-family: AdjustFam; font-size: 20pt; font-weight: bold; font-synthesis: none }", "<div><span id='s'>x</span></div>");

            Assert.Equal(PeachDrawing.Text.SyntheticStyle.None, layout.Box("s").ActualFont.SyntheticStyle);
        }

        [Fact]
        public async Task FontSynthesis_IsPartOfTheFontCacheKey()
        {
            var layout = await LayoutAsync("div { font-family: AdjustFam; font-size: 20pt; font-weight: bold }",
                "<div id='on'>x</div><div id='off' style='font-synthesis-weight: none'>x</div><div id='on2'>x</div><div id='off2' style='font-synthesis: none'>x</div>");

            var on = layout.Box("on").ActualFont;
            var off = layout.Box("off").ActualFont;

            // Same family/size/weight, different synthesis: two cached fonts, each reused for its own kind.
            Assert.NotSame(on, off);
            Assert.Same(on, layout.Box("on2").ActualFont);
            Assert.Same(off, layout.Box("off2").ActualFont);
        }

        [Fact]
        public async Task FontSynthesisSmallCaps_None_LeavesTheTextAsWritten()
        {
            var layout = await LayoutAsync("div { font-family: OtherFam; font-size: 20pt; font-variant-caps: small-caps }",
                "<div id='on'>abc</div><div id='off' style='font-synthesis-small-caps: none'>abc</div>");

            var on = Words(layout.Box("on"));
            var off = Words(layout.Box("off"));

            // Synthesized small caps upper-case the lower-case run and shrink it; with the synthesis forbidden the word is untouched.
            Assert.Equal("ABC", on.Single().Text);
            Assert.Equal(CssBox.SmallCapsFontScale, on.Single().FontSizeScale);
            Assert.Equal("abc", off.Single().Text);
            Assert.Equal(1.0, off.Single().FontSizeScale);
        }

        [Fact]
        public async Task FontSynthesisPosition_None_StopsSubSuperscriptSynthesis()
        {
            var layout = await LayoutAsync("div { font-family: NoSupsFam; font-size: 20pt; font-variant-position: super }",
                "<div id='on'>x</div><div id='off' style='font-synthesis-position: none'>x</div>");

            Assert.NotNull(layout.Box("on").DerivedStyle.SubSuperscriptSynthesis);
            Assert.Null(layout.Box("off").DerivedStyle.SubSuperscriptSynthesis);
        }

        private static List<CssRectWord> Words(CssBox box)
        {
            var words = new List<CssRectWord>();
            void Collect(CssBox b)
            {
                words.AddRange(b.Words.OfType<CssRectWord>().Where(w => w.Text != "\n"));
                foreach (var child in b.Boxes) Collect(child);
            }

            Collect(box);
            return words;
        }
    }
}
