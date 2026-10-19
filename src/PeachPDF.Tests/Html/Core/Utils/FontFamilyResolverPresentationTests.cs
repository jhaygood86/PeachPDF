using PeachDrawing.Text.Unicode;
using PeachDrawing.Core;
using PeachBrackets.Html.Utils;
using PeachPDF.Tests.TestSupport;
using System.Collections.Generic;
using System.Text;

namespace PeachPDF.Tests.Html.Core.Utils
{
    /// <summary>
    /// The font-selection order for a character asked to be drawn in a presentation (CSS
    /// <c>font-variant-emoji</c> or an explicit U+FE0E/U+FE0F), CSS Fonts 4 §5.3: a family whose font matches
    /// wins; failing that, system fallback is asked for one that does; and only when no font supports the
    /// sequence is the variation selector ignored and the first font that merely covers the character used.
    /// Driven through a scripted adapter so the answer never depends on which fonts the host has installed.
    /// </summary>
    public class FontFamilyResolverPresentationTests
    {
        private static readonly Rune Heart = new(0x2764);

        private sealed class ScriptedFont(string name, bool isColour, bool coversHeart = true) : TestFont(12)
        {
            public string Name { get; } = name;
            public override string FaceKey => Name;
            public override bool HasGlyph(Rune rune) => coversHeart;

            public override bool MatchesEmojiPresentation(Rune baseCodepoint, EmojiPresentation presentation) =>
                presentation == EmojiPresentation.NoPreference || isColour == (presentation == EmojiPresentation.Emoji);
        }

        private sealed class ScriptedRenderContext : TestPdfCanvas
        {
            public Dictionary<string, ScriptedFont> Families { get; } = new(System.StringComparer.OrdinalIgnoreCase);

            /// <summary>What system fallback answers for each requested presentation; absent means "nothing found".</summary>
            public Dictionary<EmojiPresentation, ScriptedFont> SystemFallback { get; } = [];

            public List<EmojiPresentation> SystemFallbackRequests { get; } = [];

            protected override Font? CreateFontForCodepointInt(string family, double size, PaintFontStyle style, double weight, double stretch, double? obliqueSkewSinus, Rune codepoint, string? variations) =>
                Families.GetValueOrDefault(family);

            protected override Font? CreateSystemFallbackFontForCodepointInt(double size, PaintFontStyle style, double weight, double stretch, double? obliqueSkewSinus, Rune codepoint, EmojiPresentation presentation, string? variations)
            {
                SystemFallbackRequests.Add(presentation);
                return SystemFallback.GetValueOrDefault(presentation);
            }
        }

        private static readonly ScriptedFont Colour = new("colour", isColour: true);
        private static readonly ScriptedFont Text = new("text", isColour: false);

        private static string? Resolve(ScriptedRenderContext renderContext, string stack, EmojiPresentation presentation)
        {
            var font = FontFamilyResolver.Resolve(renderContext, stack, 12, PaintFontStyle.Regular, Heart, presentation: presentation);
            return (font as ScriptedFont)?.Name;
        }

        private static ScriptedRenderContext RenderContext(params ScriptedFont[] families)
        {
            var renderContext = new ScriptedRenderContext();
            foreach (var font in families)
                renderContext.Families[font.Name] = font;
            return renderContext;
        }

        [Theory]
        [InlineData("colour, text", "colour")]
        [InlineData("text, colour", "text")]
        public void NoPreference_IsStackOrder_AndNeverAsksSystemFallback(string stack, string expected)
        {
            var renderContext = RenderContext(Colour, Text);

            Assert.Equal(expected, Resolve(renderContext, stack, EmojiPresentation.NoPreference));
            Assert.Empty(renderContext.SystemFallbackRequests);
        }

        [Theory]
        [InlineData("colour, text")]
        [InlineData("text, colour")]
        public void EmojiPresentation_SkipsAnOutlineFontThatCoversTheCharacter(string stack)
        {
            Assert.Equal("colour", Resolve(RenderContext(Colour, Text), stack, EmojiPresentation.Emoji));
        }

        [Theory]
        [InlineData("colour, text")]
        [InlineData("text, colour")]
        public void TextPresentation_SkipsAColourFontThatCoversTheCharacter(string stack)
        {
            Assert.Equal("text", Resolve(RenderContext(Colour, Text), stack, EmojiPresentation.Text));
        }

        [Fact]
        public void NoFontInTheStackMatches_SystemFallbackIsAskedForTheRequestedPresentation_BeforeTheSelectorIsIgnored()
        {
            var systemColour = new ScriptedFont("system-colour", isColour: true);
            var renderContext = RenderContext(Text);
            renderContext.SystemFallback[EmojiPresentation.Emoji] = systemColour;

            Assert.Equal("system-colour", Resolve(renderContext, "text", EmojiPresentation.Emoji));
            Assert.Equal([EmojiPresentation.Emoji], renderContext.SystemFallbackRequests);
        }

        [Theory]
        [InlineData("Emoji", "text")]
        [InlineData("Text", "colour")]
        public void NoFontSupportsTheSequence_TheSelectorIsIgnoredAndTheFirstCoveringFontIsUsed(string requested, string onlyFamily)
        {
            var presentation = System.Enum.Parse<EmojiPresentation>(requested);
            // Only one family, and it is the wrong kind; system fallback has nothing better. CSS Fonts 4 §5.3
            // step 3: match the base character alone and ignore the variation selector.
            var renderContext = RenderContext(Colour, Text);

            Assert.Equal(onlyFamily, Resolve(renderContext, onlyFamily, presentation));
            Assert.Equal([presentation], renderContext.SystemFallbackRequests);
        }

        [Fact]
        public void TheStackCoversNothing_TakesTheUnfilteredSystemFallback_WhenNoMatchingOneExists()
        {
            var anySystem = new ScriptedFont("system-any", isColour: false);
            var renderContext = RenderContext(new ScriptedFont("text", isColour: false, coversHeart: false));
            renderContext.SystemFallback[EmojiPresentation.NoPreference] = anySystem;

            // Asks first for a matching font, gets none, then takes the unfiltered system answer.
            Assert.Equal("system-any", Resolve(renderContext, "text", EmojiPresentation.Emoji));
            Assert.Equal([EmojiPresentation.Emoji, EmojiPresentation.NoPreference], renderContext.SystemFallbackRequests);
        }

        [Fact]
        public void AFontThatCannotJudgePresentation_AcceptsEveryRequest()
        {
            // Font's default answer: a font with no colour/variation-sequence data never loses a match.
            var font = new TestFont(12);

            Assert.True(font.MatchesEmojiPresentation(Heart, EmojiPresentation.Emoji));
            Assert.True(font.MatchesEmojiPresentation(Heart, EmojiPresentation.Text));
            Assert.True(font.MatchesEmojiPresentation(Heart, EmojiPresentation.NoPreference));
        }

        [Fact]
        public void NothingAnywhereCoversTheCharacter_ReturnsNull()
        {
            var renderContext = RenderContext(new ScriptedFont("text", isColour: false, coversHeart: false));

            Assert.Null(FontFamilyResolver.Resolve(renderContext, "text", 12, PaintFontStyle.Regular, Heart, presentation: EmojiPresentation.Text));
        }

        [Fact]
        public void TheSystemFallbackAnswerIsCachedPerPresentation()
        {
            var textFallback = new ScriptedFont("sys-text", isColour: false);
            var colourFallback = new ScriptedFont("sys-colour", isColour: true);
            var renderContext = RenderContext(new ScriptedFont("none", isColour: false, coversHeart: false));
            renderContext.SystemFallback[EmojiPresentation.Text] = textFallback;
            renderContext.SystemFallback[EmojiPresentation.Emoji] = colourFallback;

            Assert.Equal("sys-text", Resolve(renderContext, "none", EmojiPresentation.Text));
            Assert.Equal("sys-colour", Resolve(renderContext, "none", EmojiPresentation.Emoji));
            // A second identical request is served from the handler's cache rather than asking the adapter again.
            Assert.Equal("sys-text", Resolve(renderContext, "none", EmojiPresentation.Text));

            Assert.Equal([EmojiPresentation.Text, EmojiPresentation.Emoji], renderContext.SystemFallbackRequests);
        }
    }
}
