using PeachPDF.Fonts.OpenType;
using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.PdfSharpCore.Pdf;
using PeachPDF.Tests.TestSupport;
using PeachPDF.Text;
using System.IO;
using System.Linq;
using Xunit;

namespace PeachPDF.Tests.Html.Core
{
    /// <summary>
    /// Multi-codepoint emoji sequence composition against a <b>real</b> production color font
    /// (<see cref="BundledFonts.ColorEmojiSequences"/>), complementing
    /// <see cref="DefaultIgnorableShapingTests"/>'s hand-authored fixture. Every sequence here composes
    /// through the font's <c>ccmp</c> feature — Noto Color Emoji declares no <c>liga</c>/<c>rlig</c>/
    /// <c>clig</c> whatsoever — so a shaper that treats <c>ccmp</c> as opt-in fails all of them.
    /// </summary>
    public class EmojiSequenceCompositionTests
    {
        private static OpenTypeDescriptor Descriptor()
        {
            var face = XFontSource.GetOrCreateFrom(File.ReadAllBytes(BundledFonts.ColorEmojiSequences)).Fontface;
            return new OpenTypeDescriptor("emoji-seq-test", "emoji-seq-test", XFontStyle.Regular, face,
                new XPdfFontOptions(PdfFontEncoding.Unicode));
        }

        private static int[] Shape(string text) =>
            Descriptor().Shape(text, TextShapingFeatures.Default).Select(g => g.GlyphIndex).ToArray();

        [Theory]
        [InlineData("\U0001F3F3️‍\U0001F308", "rainbow flag: ZWJ sequence carrying a VS16")]
        [InlineData("\U0001F469‍\U0001F4BB", "woman technologist: plain ZWJ sequence")]
        [InlineData("\U0001F1FA\U0001F1F8", "US flag: regional-indicator pair")]
        [InlineData("\U0001F1EF\U0001F1F5", "Japan flag: regional-indicator pair")]
        [InlineData("\U0001F3F4\U000E0067\U000E0062\U000E0073\U000E0063\U000E0074\U000E007F",
            "Scotland flag: tag sequence")]
        public void Sequence_ComposesToASingleGlyph(string sequence, string description)
        {
            var glyphs = Shape(sequence);

            Assert.True(glyphs.Length == 1,
                $"{description}: expected one composed glyph, got {glyphs.Length} ({string.Join(",", glyphs)})");
            Assert.NotEqual(0, glyphs[0]);
        }

        [Theory]
        [InlineData(0x1F3FB)] // TYPE-1-2
        [InlineData(0x1F3FC)] // TYPE-3
        [InlineData(0x1F3FD)] // TYPE-4
        [InlineData(0x1F3FE)] // TYPE-5
        [InlineData(0x1F3FF)] // TYPE-6
        public void SkinToneModifier_ComposesWithItsBaseEmoji(int modifier)
        {
            // A Fitzpatrick modifier is NOT default-ignorable - it has a real swatch glyph of its own -
            // so an unapplied ccmp does not hide it, it *draws* it: the pre-fix rendering of U+1F44D
            // U+1F3FD was a yellow thumbs-up followed by a bare brown square. Asserting the composed
            // form differs from both inputs is what rules that out.
            const string thumbsUp = "\U0001F44D";
            var modified = Shape(thumbsUp + char.ConvertFromUtf32(modifier));

            Assert.Single(modified);
            Assert.NotEqual(Shape(thumbsUp)[0], modified[0]);
            Assert.NotEqual(Shape(char.ConvertFromUtf32(modifier))[0], modified[0]);
        }

        [Fact]
        public void EachSkinTone_ComposesToADistinctGlyph()
        {
            // Five modifiers must select five different glyphs - a single shared result would mean the
            // tone is being dropped or collapsed rather than actually applied.
            var composed = new[] { 0x1F3FB, 0x1F3FC, 0x1F3FD, 0x1F3FE, 0x1F3FF }
                .Select(m => Shape("\U0001F44D" + char.ConvertFromUtf32(m)).Single())
                .ToArray();

            Assert.Equal(5, composed.Distinct().Count());
        }

        [Fact]
        public void VariationSelectorAfterABaseEmoji_AddsNoGlyph()
        {
            // U+FE0F is absent from this font's cmap (as it is upstream) and must contribute nothing.
            Assert.Equal(Shape("❤"), Shape("❤️"));
        }
    }
}
