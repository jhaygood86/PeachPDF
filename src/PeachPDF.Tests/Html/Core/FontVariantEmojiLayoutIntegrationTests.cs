using PeachPDF.Adapters;
using PeachPDF.CSS;
using PeachPDF.Html.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.Tests.TestSupport;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Html.Core
{
    /// <summary>
    /// End-to-end coverage of CSS Fonts 4 <c>font-variant-emoji</c> and the U+FE0E/U+FE0F selectors through
    /// real layout with two genuinely different fonts covering U+2764 HEAVY BLACK HEART: Noto Color Emoji (a
    /// COLR/CPAL colour font) and Source Sans 3 (an outline font). Both cover the character, so the only thing
    /// that can pick the right one is the requested presentation - a no-op implementation resolves every case
    /// to whichever family comes first in the stack and fails these assertions.
    /// </summary>
    public class FontVariantEmojiLayoutIntegrationTests
    {
        private const string Heart = "&#x2764;";
        private const string TextSelector = "&#xFE0E;";
        private const string EmojiSelector = "&#xFE0F;";
        private const string ThumbsUp = "&#x1F44D;";

        private sealed record Layout(PdfSharpAdapter Adapter, CssBox Root)
        {
            public string ColourName => FontName("ColourFam");
            public string TextName => FontName("TextFam");

            private string FontName(string family) => ((FontAdapter)Adapter.GetFont(family, 12, RFontStyle.Regular)!).Font.Name;

            public CssBox Box(string id) => Find(Root, id) ?? throw new InvalidOperationException($"no box #{id}");

            /// <summary>The font each word of box <paramref name="id"/> is drawn in, by real font name.</summary>
            public IReadOnlyList<string> WordFonts(string id)
            {
                var box = Box(id);
                var words = new List<CssRectWord>();
                Collect(box, words);
                return words.Select(w => ((FontAdapter)CssBox.ResolveWordFont(w, w.OwnerBox)).Font.Name).ToList();
            }

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

            private static void Collect(CssBox box, List<CssRectWord> words)
            {
                words.AddRange(box.Words.OfType<CssRectWord>().Where(w => w.Text != "\n"));
                foreach (var child in box.Boxes) Collect(child, words);
            }
        }

        private static async Task<Layout> LayoutAsync(string css, string body)
        {
            var adapter = new PdfSharpAdapter();
            await BundledFonts.RegisterFont(adapter, BundledFonts.ColorEmoji, "ColourFam");
            await BundledFonts.RegisterFont(adapter, BundledFonts.Ttf, "TextFam");

            var container = new HtmlContainerInt(adapter);
            await container.SetHtml($"<html><head><style>{css}</style></head><body>{body}</body></html>", null);

            var size = new XSize(595, 842);
            container.PageSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);
            container.MaxSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);

            var measure = XGraphics.CreateMeasureContext(size, XGraphicsUnit.Point, XPageDirection.Downwards);
            using var graphics = new GraphicsAdapter(adapter, measure, 1.0);
            await container.PerformLayout(graphics);

            return new Layout(adapter, container.Root!);
        }

        private const string ColourFirst = "ColourFam, TextFam";
        private const string TextFirst = "TextFam, ColourFam";

        // ---- the property --------------------------------------------------------------------------

        [Theory]
        [InlineData("font-variant-emoji: emoji", "Emoji")]
        [InlineData("font-variant-emoji: text", "Text")]
        [InlineData("font-variant-emoji: unicode", "Unicode")]
        [InlineData("font-variant-emoji: normal", "Normal")]
        [InlineData("font-variant: emoji", "Emoji")]
        [InlineData("font-variant: text", "Text")]
        [InlineData("font-variant: unicode", "Unicode")]
        // A keyword that is not one of the four is dropped, leaving the initial value.
        [InlineData("font-variant-emoji: banana", "Normal")]
        public async Task TheDeclaration_ReachesTheBoxAsTheExpectedKeyword(string declaration, string expected)
        {
            // Asserts non-initial values on purpose: a keyword-only property whose test used its own initial
            // value would pass even if the whole declaration were dropped before it reached CssBox.
            var layout = await LayoutAsync("", $"<p id=\"p\" style=\"{declaration}\">x</p>");

            Assert.Equal(Enum.Parse<FontVariantEmojiMode>(expected), layout.Box("p").ActualFontVariantEmoji);
        }

        [Fact]
        public async Task TheProperty_IsInherited()
        {
            var layout = await LayoutAsync("", "<div style=\"font-variant-emoji: emoji\"><p id=\"p\">x</p></div>");

            Assert.Equal(FontVariantEmojiMode.Emoji, layout.Box("p").ActualFontVariantEmoji);
        }

        [Fact]
        public async Task TheFontShorthand_ResetsIt()
        {
            // CSS Fonts 4 §7.7: `font` resets every font-variant-* longhand, emoji presentation included.
            var layout = await LayoutAsync("", "<div style=\"font-variant-emoji: emoji\"><p id=\"p\" style=\"font: 12px serif\">x</p></div>");

            Assert.Equal(FontVariantEmojiMode.Normal, layout.Box("p").ActualFontVariantEmoji);
        }

        [Fact]
        public async Task TheFontVariantShorthand_ResetsItWhenItIsNotNamed()
        {
            var layout = await LayoutAsync("", "<div style=\"font-variant-emoji: emoji\"><p id=\"p\" style=\"font-variant: small-caps\">x</p></div>");

            Assert.Equal(FontVariantEmojiMode.Normal, layout.Box("p").ActualFontVariantEmoji);
        }

        [Fact]
        public async Task SupportsQuery_AcceptsTheProperty()
        {
            var layout = await LayoutAsync("@supports (font-variant-emoji: emoji) { p { font-variant-emoji: text } }", "<p id=\"p\">x</p>");

            Assert.Equal(FontVariantEmojiMode.Text, layout.Box("p").ActualFontVariantEmoji);
        }

        // ---- explicit variation selectors (no property) ----------------------------------------------

        [Fact]
        public async Task NoSelectorNoProperty_IsStackOrder_AsBefore()
        {
            var layout = await LayoutAsync("", $"<p id=\"a\" style=\"font-family: {ColourFirst}\">{Heart}</p><p id=\"b\" style=\"font-family: {TextFirst}\">{Heart}</p>");

            Assert.Equal([layout.ColourName], layout.WordFonts("a"));
            Assert.Equal([layout.TextName], layout.WordFonts("b"));
        }

        [Fact]
        public async Task Fe0e_SelectsTheOutlineFont_EvenWhenTheColourFontComesFirst()
        {
            var layout = await LayoutAsync("", $"<p id=\"a\" style=\"font-family: {ColourFirst}\">{Heart}{TextSelector}</p><p id=\"b\" style=\"font-family: {TextFirst}\">{Heart}{TextSelector}</p>");

            Assert.Equal([layout.TextName], layout.WordFonts("a"));
            Assert.Equal([layout.TextName], layout.WordFonts("b"));
        }

        [Fact]
        public async Task Fe0f_SelectsTheColourFont_EvenWhenTheOutlineFontComesFirst()
        {
            var layout = await LayoutAsync("", $"<p id=\"a\" style=\"font-family: {ColourFirst}\">{Heart}{EmojiSelector}</p><p id=\"b\" style=\"font-family: {TextFirst}\">{Heart}{EmojiSelector}</p>");

            Assert.Equal([layout.ColourName], layout.WordFonts("a"));
            Assert.Equal([layout.ColourName], layout.WordFonts("b"));
        }

        [Fact]
        public async Task ASelectorAfterACharacterThatIsNotAParticipant_HasNoEffectOnFontSelection()
        {
            // U+1F600 is an emoji but has no text form to choose (it is not in emoji-variation-sequences.txt),
            // so a text selector after it is ignored: the colour font - the only one of the two that covers
            // it - is used, rather than the selector sending the search off to a system font for a "text"
            // rendering of it.
            var layout = await LayoutAsync("", $"<p id=\"a\" style=\"font-family: {ColourFirst}\">&#x1F600;{TextSelector}</p><p id=\"b\" style=\"font-family: {TextFirst}\">&#x1F600;{TextSelector}</p>");

            Assert.Equal([layout.ColourName], layout.WordFonts("a"));
            Assert.Equal([layout.ColourName], layout.WordFonts("b"));
        }

        [Fact]
        public async Task TheSelectorStaysInTheFragmentOfTheCharacterItFollows()
        {
            // Both hearts are separated only by the selector of the first one: each fragment keeps its own.
            var layout = await LayoutAsync("", $"<p id=\"p\" style=\"font-family: {ColourFirst}\">{Heart}{TextSelector}{Heart}</p>");

            Assert.Equal([layout.TextName, layout.ColourName], layout.WordFonts("p"));
        }

        // ---- font-variant-emoji --------------------------------------------------------------------------

        [Theory]
        [InlineData("text", ColourFirst, "Text")]
        [InlineData("text", TextFirst, "Text")]
        [InlineData("emoji", ColourFirst, "Colour")]
        [InlineData("emoji", TextFirst, "Colour")]
        // U+2764 is text-default (no Emoji_Presentation), so unicode draws it as text.
        [InlineData("unicode", ColourFirst, "Text")]
        [InlineData("unicode", TextFirst, "Text")]
        // normal makes no choice: stack order, as before the property existed.
        [InlineData("normal", ColourFirst, "Colour")]
        [InlineData("normal", TextFirst, "Text")]
        public async Task TheProperty_SelectsThePresentation_ForABareCharacter(string value, string stack, string expected)
        {
            var layout = await LayoutAsync("", $"<p id=\"p\" style=\"font-family: {stack}; font-variant-emoji: {value}\">{Heart}</p>");

            Assert.Equal([expected == "Colour" ? layout.ColourName : layout.TextName], layout.WordFonts("p"));
        }

        [Theory]
        [InlineData("text", ColourFirst)]
        [InlineData("text", TextFirst)]
        public async Task AnExplicitFe0f_OverridesFontVariantEmojiText(string value, string stack)
        {
            var layout = await LayoutAsync("", $"<p id=\"p\" style=\"font-family: {stack}; font-variant-emoji: {value}\">{Heart}{EmojiSelector}</p>");

            Assert.Equal([layout.ColourName], layout.WordFonts("p"));
        }

        [Theory]
        [InlineData("emoji", ColourFirst)]
        [InlineData("emoji", TextFirst)]
        public async Task AnExplicitFe0e_OverridesFontVariantEmojiEmoji(string value, string stack)
        {
            var layout = await LayoutAsync("", $"<p id=\"p\" style=\"font-family: {stack}; font-variant-emoji: {value}\">{Heart}{TextSelector}</p>");

            Assert.Equal([layout.TextName], layout.WordFonts("p"));
        }

        [Fact]
        public async Task Unicode_DrawsAnEmojiDefaultCharacterAsEmoji_EvenWhenTheOutlineFontComesFirst()
        {
            // U+1F44D THUMBS UP SIGN has Emoji_Presentation=Yes and is a participant (it has a variation
            // sequence); Noto Color Emoji and Source Sans 3 do not both cover it, so use the mono Noto Emoji.
            var adapter = new PdfSharpAdapter();
            await BundledFonts.RegisterFont(adapter, BundledFonts.ColorEmoji, "ColourFam");
            await BundledFonts.RegisterFont(adapter, BundledFonts.Emoji, "OutlineEmojiFam");

            var container = new HtmlContainerInt(adapter);
            await container.SetHtml($"<html><body><p id=\"p\" style=\"font-family: OutlineEmojiFam, ColourFam; font-variant-emoji: unicode\">{ThumbsUp}</p></body></html>", null);

            var size = new XSize(595, 842);
            container.PageSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);
            container.MaxSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);
            var measure = XGraphics.CreateMeasureContext(size, XGraphicsUnit.Point, XPageDirection.Downwards);
            using var graphics = new GraphicsAdapter(adapter, measure, 1.0);
            await container.PerformLayout(graphics);

            var layout = new Layout(adapter, container.Root!);
            var expected = ((FontAdapter)adapter.GetFont("ColourFam", 12, RFontStyle.Regular)!).Font.Name;

            Assert.Equal([expected], layout.WordFonts("p"));
        }

        [Fact]
        public async Task ACharacterThatIsNotAParticipant_IsNeverSteeredByTheProperty()
        {
            // U+1F600 GRINNING FACE: both the property values leave stack order alone.
            var layout = await LayoutAsync("", $"<p id=\"a\" style=\"font-family: {TextFirst}; font-variant-emoji: emoji\">&#x1F600;</p><p id=\"b\" style=\"font-family: {ColourFirst}; font-variant-emoji: text\">&#x1F600;</p>");

            // Source Sans 3 has no glyph for U+1F600, so the colour font supplies it in both stacks.
            Assert.Equal([layout.ColourName], layout.WordFonts("a"));
            Assert.Equal([layout.ColourName], layout.WordFonts("b"));
        }

        [Fact]
        public async Task PlainTextWithNoEmoji_StillTakesTheFastPath_AsASingleWord()
        {
            var layout = await LayoutAsync("", "<p id=\"p\" style=\"font-family: TextFam, ColourFam; font-variant-emoji: emoji\">Hello</p>");

            var words = new List<CssRectWord>();
            var box = layout.Box("p");
            words.AddRange(box.Words.OfType<CssRectWord>());
            foreach (var child in box.Boxes) words.AddRange(child.Words.OfType<CssRectWord>());

            Assert.Single(words);
            Assert.False(words[0].UsesPerCodepointFont);
        }
    }
}
