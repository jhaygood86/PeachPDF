using PeachPDF.Adapters;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.PdfSharpCore.Utils;
using PeachPDF.Tests.TestSupport;
using System;
using System.IO;
using System.Linq;
using System.Text;

using PeachPDF.Fonts;

namespace PeachPDF.Tests.Html.Core
{
    /// <summary>
    /// End-to-end coverage of <c>@font-face</c> <c>unicode-range</c>: two genuinely different fonts
    /// (Source Sans 3, a glyf TTF, and Source Code Pro, a CFF OTF) are declared under ONE CSS family
    /// with disjoint ranges - uppercase to one, lowercase to the other. Because both fonts cover both
    /// cases, the only thing that can route each character to the right file is honoring
    /// <c>unicode-range</c>; a no-op implementation would resolve the whole run to a single face and
    /// these assertions would fail.
    /// </summary>
    public class UnicodeRangeLayoutIntegrationTests
    {
        private static string B64(string path) => Convert.ToBase64String(File.ReadAllBytes(path));

        private static CssBox? FindByTag(CssBox box, string tag)
        {
            if (box.HtmlTag?.Name.Equals(tag, StringComparison.OrdinalIgnoreCase) == true)
                return box;
            foreach (var child in box.Boxes)
            {
                var found = FindByTag(child, tag);
                if (found != null) return found;
            }
            return null;
        }

        private static async Task<CssBox> LayoutParagraph(string html)
        {
            var adapter = new PdfSharpAdapter();
            var container = new HtmlContainerInt(adapter);
            await container.SetHtml(html, null);

            var size = new XSize(595, 842);
            container.PageSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);
            container.MaxSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);

            var measure = XGraphics.CreateMeasureContext(size, XGraphicsUnit.Point, XPageDirection.Downwards);
            using var graphics = new GraphicsAdapter(adapter, measure, 1.0);
            await container.PerformLayout(graphics);

            return FindByTag(container.Root!, "p")!;
        }

        private static void CollectWords(CssBox box, System.Collections.Generic.List<CssRectWord> words)
        {
            words.AddRange(box.Words.OfType<CssRectWord>().Where(w => w.Text != "\n"));
            foreach (var child in box.Boxes)
                CollectWords(child, words);
        }

        private static System.Collections.Generic.List<CssRectWord> WordsOf(CssBox p)
        {
            var words = new System.Collections.Generic.List<CssRectWord>();
            CollectWords(p, words);
            return words;
        }

        private static string ResolvedFamilyName(CssBox ownerBox, char c)
        {
            var font = ownerBox.ActualFontForCodepoint(new Rune(c));
            return ((FontAdapter)font).Font.Name;
        }

        [Fact]
        public async Task DisjointRanges_SplitWordAcrossTwoFaces_EachCharacterUsesItsDeclaringFont()
        {
            var upperFamily = TtfFontDescription.LoadDescription(BundledFonts.Ttf).FontFamilyInvariantCulture; // Source Sans 3
            var lowerFamily = TtfFontDescription.LoadDescription(BundledFonts.Otf).FontFamilyInvariantCulture; // Source Code Pro
            Assert.NotEqual(upperFamily, lowerFamily);

            var html = $@"<!DOCTYPE html>
<html><head><style>
@font-face {{ font-family: 'Split'; src: url('data:font/truetype;base64,{B64(BundledFonts.Ttf)}') format('truetype'); unicode-range: U+41-5A; }}
@font-face {{ font-family: 'Split'; src: url('data:font/opentype;base64,{B64(BundledFonts.Otf)}') format('opentype'); unicode-range: U+61-7A; }}
body {{ font-family: 'Split'; font-size: 14pt; }}
p {{ width: 400px; }}
</style></head>
<body><p>AaBb</p></body>
</html>";

            var p = await LayoutParagraph(html);

            // "AaBb" alternates uppercase/lowercase, so it splits into four single-character fragments,
            // each resolved per codepoint and marked as such.
            var words = WordsOf(p);
            Assert.Equal(new[] { "A", "a", "B", "b" }, words.Select(w => w.Text));
            Assert.All(words, w => Assert.True(w.UsesPerCodepointFont));

            // Only the first fragment may start a line; the rest are glued so the split never introduces a
            // spurious wrap opportunity mid-"word".
            Assert.False(words[0].SuppressWrapBefore);
            Assert.All(words.Skip(1), w => Assert.True(w.SuppressWrapBefore));

            // The decisive assertion: each character resolves to the face whose unicode-range declared it.
            var owner = words[0].OwnerBox;
            Assert.Equal(upperFamily, ResolvedFamilyName(owner, 'A'));
            Assert.Equal(upperFamily, ResolvedFamilyName(owner, 'B'));
            Assert.Equal(lowerFamily, ResolvedFamilyName(owner, 'a'));
            Assert.Equal(lowerFamily, ResolvedFamilyName(owner, 'b'));
        }

        [Fact]
        public async Task PlainText_NoUnicodeRange_TakesFastPath_SingleWordNotSplit()
        {
            // A single @font-face with no unicode-range, fully covering the text, must behave exactly as
            // before: one word, no per-codepoint resolution.
            var html = $@"<!DOCTYPE html>
<html><head><style>
@font-face {{ font-family: 'Plain'; src: url('data:font/truetype;base64,{B64(BundledFonts.Ttf)}') format('truetype'); }}
body {{ font-family: 'Plain', serif; font-size: 14pt; }}
p {{ width: 400px; }}
</style></head>
<body><p>Hello</p></body>
</html>";

            var p = await LayoutParagraph(html);
            var words = WordsOf(p);

            Assert.Single(words);
            Assert.Equal("Hello", words[0].Text);
            Assert.False(words[0].UsesPerCodepointFont);
        }

        [Fact]
        public async Task CoverageFallback_AcrossFontFamilyStack_UsesNextFamilyForUncoveredCodepoint()
        {
            // Primary family declares a unicode-range covering only uppercase; the fallback family declares
            // no range, so its coverage comes from its cmap. A lowercase character is out of the primary's
            // range and must fall back to the second family - the browser "first available font that can
            // render this character" rule, exercised across the font-family stack (and the cmap-coverage
            // path for the rangeless fallback).
            var primaryFamily = TtfFontDescription.LoadDescription(BundledFonts.Ttf).FontFamilyInvariantCulture; // Source Sans 3
            var fallbackFamily = TtfFontDescription.LoadDescription(BundledFonts.Otf).FontFamilyInvariantCulture; // Source Code Pro

            var html = $@"<!DOCTYPE html>
<html><head><style>
@font-face {{ font-family: 'Prim'; src: url('data:font/truetype;base64,{B64(BundledFonts.Ttf)}') format('truetype'); unicode-range: U+41-5A; }}
@font-face {{ font-family: 'Fall'; src: url('data:font/opentype;base64,{B64(BundledFonts.Otf)}') format('opentype'); }}
body {{ font-family: 'Prim', 'Fall'; font-size: 14pt; }}
p {{ width: 400px; }}
</style></head>
<body><p>Aa</p></body>
</html>";

            var p = await LayoutParagraph(html);
            var owner = WordsOf(p)[0].OwnerBox;

            Assert.Equal(primaryFamily, ResolvedFamilyName(owner, 'A'));  // in Prim's range
            Assert.Equal(fallbackFamily, ResolvedFamilyName(owner, 'a')); // out of range -> Fall (via its cmap coverage)
        }

        [Fact]
        public async Task CoverageFallback_ToGenericFamily_ResolvesTheMappedFont()
        {
            // A generic fallback ('serif') is not a real registered family - it is mapped to an installed
            // font. Per-codepoint matching must apply that mapping, otherwise a character outside the ranged
            // family's range would wrongly stay on the ranged family (its box default) instead of falling
            // back. We assert the out-of-range character does NOT resolve to the ranged family.
            var rangedFamily = TtfFontDescription.LoadDescription(BundledFonts.Ttf).FontFamilyInvariantCulture;

            var html = $@"<!DOCTYPE html>
<html><head><style>
@font-face {{ font-family: 'Digits'; src: url('data:font/truetype;base64,{B64(BundledFonts.Ttf)}') format('truetype'); unicode-range: U+30-39; }}
body {{ font-family: 'Digits', serif; font-size: 14pt; }}
p {{ width: 400px; }}
</style></head>
<body><p>1x</p></body>
</html>";

            var p = await LayoutParagraph(html);
            var owner = WordsOf(p)[0].OwnerBox;

            Assert.Equal(rangedFamily, ResolvedFamilyName(owner, '1')); // in Digits' range
            Assert.NotEqual(rangedFamily, ResolvedFamilyName(owner, 'x')); // out of range -> mapped 'serif', not Digits
        }

        [Fact]
        public async Task SmallCaps_ComposesWith_PerCodepointSplit()
        {
            // font-variant: small-caps splits by letter case AND the ranged family forces per-codepoint
            // resolution: the two mechanisms must compose - fragments carry both the small-caps size scale
            // and the per-codepoint marker, still glued so the split introduces no new wrap opportunity.
            var html = $@"<!DOCTYPE html>
<html><head><style>
@font-face {{ font-family: 'Caps'; src: url('data:font/truetype;base64,{B64(BundledFonts.Ttf)}') format('truetype'); unicode-range: U+41-5A; }}
body {{ font-family: 'Caps'; font-size: 14pt; font-variant: small-caps; }}
p {{ width: 400px; }}
</style></head>
<body><p>aX</p></body>
</html>";

            var p = await LayoutParagraph(html);
            var words = WordsOf(p);

            // 'a' becomes an upper-cased small-caps run (scaled), 'X' stays full size; both are per-codepoint.
            Assert.Equal(new[] { "A", "X" }, words.Select(w => w.Text));
            Assert.All(words, w => Assert.True(w.UsesPerCodepointFont));
            Assert.True(words[0].FontSizeScale < 1.0);   // small-caps scale on the originally-lowercase run
            Assert.Equal(1.0, words[1].FontSizeScale);
            Assert.False(words[0].SuppressWrapBefore);
            Assert.True(words[1].SuppressWrapBefore);
        }
    }
}
