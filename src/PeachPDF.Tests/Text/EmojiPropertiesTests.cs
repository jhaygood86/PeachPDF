using PeachPDF.CSS;
using PeachPDF.Text;
using System;

namespace PeachPDF.Tests.Text
{
    /// <summary>
    /// The Unicode emoji tables (generated from <c>emoji-variation-sequences.txt</c> and
    /// <c>emoji-data.txt</c>) and the rule that turns a <c>font-variant-emoji</c> value plus an explicit
    /// variation selector into a presentation - CSS Fonts 4 \u00A79.3.
    /// </summary>
    public class EmojiPropertiesTests
    {
        [Theory]
        [InlineData(0x2764, true)]   // HEAVY BLACK HEART: text-default, has a variation sequence
        [InlineData(0x263A, true)]   // WHITE SMILING FACE
        [InlineData(0x00A9, true)]   // COPYRIGHT SIGN
        [InlineData(0x231A, true)]   // WATCH: emoji-default, has a variation sequence
        [InlineData(0x1F44D, true)]  // THUMBS UP SIGN
        [InlineData(0x0023, true)]   // NUMBER SIGN: the keycap base
        [InlineData(0x0030, true)]   // DIGIT ZERO
        [InlineData(0x1F600, false)] // GRINNING FACE: emoji, but no text form to choose - not a participant
        [InlineData(0x1F525, false)] // FIRE
        [InlineData(0x0041, false)]  // an ordinary letter
        [InlineData(0x0022, false)]
        [InlineData(0x10FFFF, false)]
        public void IsPresentationParticipant_MatchesTheVariationSequenceBases(int codepoint, bool expected)
        {
            Assert.Equal(expected, EmojiProperties.IsPresentationParticipant(codepoint));
        }

        [Theory]
        [InlineData(0x231A, true)]   // WATCH
        [InlineData(0x1F44D, true)]  // THUMBS UP SIGN
        [InlineData(0x1F600, true)]  // GRINNING FACE
        [InlineData(0x2B50, true)]   // WHITE MEDIUM STAR
        [InlineData(0x2764, false)]  // HEAVY BLACK HEART is text-default
        [InlineData(0x00A9, false)]
        [InlineData(0x0041, false)]
        public void HasEmojiPresentationProperty_SeparatesEmojiDefaultFromTextDefault(int codepoint, bool expected)
        {
            Assert.Equal(expected, EmojiProperties.HasEmojiPresentationProperty(codepoint));
        }

        [Theory]
        // normal leaves the choice to the user agent; the explicit selector still decides.
        [InlineData("Normal", 0x2764, 0, "NoPreference")]
        [InlineData("Normal", 0x2764, 0xFE0F, "Emoji")]
        [InlineData("Normal", 0x2764, 0xFE0E, "Text")]
        // text / emoji act as if that selector had been appended...
        [InlineData("Text", 0x2764, 0, "Text")]
        [InlineData("Emoji", 0x2764, 0, "Emoji")]
        // ...but an explicit selector in the content opts out of the default.
        [InlineData("Text", 0x2764, 0xFE0F, "Emoji")]
        [InlineData("Emoji", 0x2764, 0xFE0E, "Text")]
        // unicode follows Emoji_Presentation: emoji-default is emoji, text-default is text.
        [InlineData("Unicode", 0x231A, 0, "Emoji")]
        [InlineData("Unicode", 0x2764, 0, "Text")]
        [InlineData("Unicode", 0x2764, 0xFE0F, "Emoji")]
        [InlineData("Unicode", 0x231A, 0xFE0E, "Text")]
        // a character that is not a participant is never affected - by the property or by a selector.
        [InlineData("Text", 0x1F600, 0, "NoPreference")]
        [InlineData("Emoji", 0x0041, 0, "NoPreference")]
        [InlineData("Normal", 0x0041, 0xFE0F, "NoPreference")]
        // no variation selector other than FE0E/FE0F counts.
        [InlineData("Normal", 0x2764, 0xFE00, "NoPreference")]
        [InlineData("Emoji", 0x2764, 0xFE00, "Emoji")]
        public void Resolve_FollowsCssFonts4(string mode, int baseCodepoint, int selector, string expected)
        {
            Assert.Equal(Enum.Parse<EmojiPresentation>(expected), EmojiProperties.Resolve(Enum.Parse<FontVariantEmojiMode>(mode), baseCodepoint, selector));
        }

        [Theory]
        [InlineData("\u2764", 0, "Normal", "NoPreference")]
        [InlineData("\u2764\uFE0F", 0, "Normal", "Emoji")]
        [InlineData("\u2764\uFE0E", 0, "Emoji", "Text")]
        [InlineData("x\u2764\uFE0F", 1, "Normal", "Emoji")]
        [InlineData("x\u2764\uFE0F", 0, "Emoji", "NoPreference")]
        // Only the selector directly after the base counts - a later one, and a trailing second one, do not.
        [InlineData("\u2764\u200D\uFE0F", 0, "Normal", "NoPreference")]
        [InlineData("\u2764\uFE0E\uFE0F", 0, "Normal", "Text")]
        // An astral base decodes as one character, so its selector is read past the surrogate pair.
        [InlineData("\U0001F44D\uFE0E", 0, "Normal", "Text")]
        // A base that continues into an emoji modifier, a keycap or a ZWJ sequence is part of a longer emoji
        // sequence: the property does not split it (an explicit selector still applies).
        [InlineData("👍🏽", 0, "Text", "NoPreference")]
        [InlineData("1⃣", 0, "Emoji", "NoPreference")]
        [InlineData("❤‍🔥", 0, "Text", "NoPreference")]
        [InlineData("❤️‍🔥", 0, "Text", "Emoji")]
        [InlineData("❤x", 0, "Text", "Text")]
        // Out-of-range indexes and a lone surrogate are "no preference", not an exception.
        [InlineData("", 0, "Emoji", "NoPreference")]
        [InlineData("\u2764", 5, "Emoji", "NoPreference")]
        [InlineData("\u2764", -1, "Emoji", "NoPreference")]
        [InlineData("\uD83D", 0, "Emoji", "NoPreference")]
        public void ResolveAt_ReadsTheSelectorThatDirectlyFollowsTheBase(string text, int index, string mode, string expected)
        {
            Assert.Equal(Enum.Parse<EmojiPresentation>(expected), EmojiProperties.ResolveAt(Enum.Parse<FontVariantEmojiMode>(mode), text, index));
        }

        [Theory]
        [InlineData(0xFE0E, true)]
        [InlineData(0xFE0F, true)]
        [InlineData(0xFE00, false)]
        [InlineData(0x200D, false)]
        [InlineData(0x2764, false)]
        public void IsPresentationSelector_IsExactlyFe0eAndFe0f(int codepoint, bool expected)
        {
            Assert.Equal(expected, EmojiProperties.IsPresentationSelector(codepoint));
        }

        [Fact]
        public void SelectorFor_NamesTheSelectorThatExpressesEachPresentation()
        {
            Assert.Equal(0xFE0E, EmojiProperties.SelectorFor(EmojiPresentation.Text));
            Assert.Equal(0xFE0F, EmojiProperties.SelectorFor(EmojiPresentation.Emoji));
            Assert.Equal(0, EmojiProperties.SelectorFor(EmojiPresentation.NoPreference));
        }
    }
}
