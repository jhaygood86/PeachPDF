using PeachDrawing.Text.Unicode;
using System.Text;

namespace PeachDrawing.Text.Tests.PublicApi
{
    /// <summary>
    /// The public <c>PeachDrawing.Text.Unicode</c> entry points, exercised the way a consumer outside the assembly
    /// would call them: through the public surface only, with no knowledge of the algorithms behind it.
    /// </summary>
    public class UnicodePublicApiTests
    {
        private const string Alef = "א";

        [Fact]
        public void Bidi_Analyze_LeftToRightText_ResolvesLevelZero()
        {
            var analysis = Bidi.Analyze("abc", BaseDirection.Ltr);

            Assert.Equal(new byte[] { 0, 0, 0 }, analysis.Levels);
            Assert.Equal(0, analysis.ParagraphLevel);
            Assert.False(analysis.IsParagraphRtl);
        }

        [Fact]
        public void Bidi_Analyze_AutoDirection_TakesTheFirstStrongCharacter()
        {
            var analysis = Bidi.Analyze(Alef + Alef + "ab", BaseDirection.Auto);

            Assert.True(analysis.IsParagraphRtl);
            Assert.Equal(1, analysis.ParagraphLevel);
        }

        [Fact]
        public void Bidi_Analyze_AnEmbeddingSpan_PushesWithoutAControlCharacter()
        {
            var analysis = Bidi.Analyze("ab", BaseDirection.Ltr, [new EmbeddingSpan(0, 2, ExplicitPush.Rlo)]);

            Assert.Equal(new byte[] { 1, 1 }, analysis.Levels);
            Assert.Equal(2, new EmbeddingSpan(0, 2, ExplicitPush.Rlo).End);
        }

        [Fact]
        public void Bidi_ReorderLine_PutsTheRunsOfARightToLeftParagraphInVisualOrder()
        {
            var analysis = Bidi.Analyze("ab " + Alef + Alef, BaseDirection.Rtl);

            var runs = Bidi.ReorderLine(analysis.Levels, 0, analysis.Levels.Length);

            Assert.Equal(2, runs.Count);
            Assert.True(runs[0].IsRtl);
            Assert.Equal(new BidiRun(2, 3, 1), runs[0]);
            Assert.False(runs[1].IsRtl);
            Assert.Equal(0, runs[1].Start);
        }

        [Theory]
        [InlineData(-1, 1)]
        [InlineData(0, -1)]
        [InlineData(0, 4)]
        [InlineData(2, 2)]
        public void Bidi_ReorderLine_RejectsALineOutsideTheLevels(int lineStart, int lineLength)
        {
            var levels = new byte[] { 0, 0, 1 };

            Assert.Throws<ArgumentOutOfRangeException>(() => Bidi.ReorderLine(levels, lineStart, lineLength));
        }

        [Fact]
        public void Bidi_ReorderLine_AcceptsALineThatEndsExactlyAtTheEnd()
        {
            var runs = Bidi.ReorderLine([0, 0, 1], 1, 2);

            Assert.Equal(2, runs.Count);
        }

        [Fact]
        public void Bidi_ClassOf_ReportsTheBidiClassProperty()
        {
            Assert.Equal(BidiClass.L, Bidi.ClassOf(new Rune('a')));
            Assert.Equal(BidiClass.R, Bidi.ClassOf(new Rune(0x05D0)));
            Assert.Equal(BidiClass.AL, Bidi.ClassOf(new Rune(0x0627)));
            Assert.Equal(BidiClass.EN, Bidi.ClassOf(new Rune('1')));
            Assert.Equal(BidiClass.WS, Bidi.ClassOf(new Rune(' ')));
        }

        [Fact]
        public void Bidi_TryGetMirror_FindsTheCounterpartOfABracket()
        {
            Assert.True(Bidi.TryGetMirror(new Rune('('), out var mirror));
            Assert.Equal(new Rune(')'), mirror);
            Assert.False(Bidi.TryGetMirror(new Rune('a'), out var none));
            Assert.Equal(default, none);
        }

        [Fact]
        public void Bidi_Mirror_ReversesAndMirrorsAnOddLevelRunOnly()
        {
            Assert.Equal("(ba)", Bidi.Mirror("(ab)", 1));
            Assert.Same("(ab)", Bidi.Mirror("(ab)", 0));
        }

        [Fact]
        public void Bidi_Reverse_KeepsSurrogatePairsWhole()
        {
            Assert.Equal("b\U0001F600a", Bidi.Reverse("a\U0001F600b"));
        }

        [Fact]
        public void Bidi_RejectsNullArguments()
        {
            Assert.Throws<ArgumentNullException>(() => Bidi.Analyze(null!, BaseDirection.Ltr));
            Assert.Throws<ArgumentNullException>(() => Bidi.ReorderLine(null!, 0, 0));
            Assert.Throws<ArgumentNullException>(() => Bidi.Mirror(null!, 1));
            Assert.Throws<ArgumentNullException>(() => Bidi.Reverse(null!));
        }

        [Fact]
        public void Scripts_Of_NamesTheScriptProperty()
        {
            Assert.Equal("Latin", Scripts.Of(new Rune('a')));
            Assert.Equal("Arabic", Scripts.Of(0x0627));
            Assert.Equal(Scripts.Common, Scripts.Of(' '));
            Assert.Equal(Scripts.Inherited, Scripts.Of(0x0301));
            Assert.Equal(Scripts.Unknown, Scripts.Of(0x378));
        }

        [Fact]
        public void Scripts_Resolve_GivesSharedCharactersTheScriptAroundThem()
        {
            var resolved = Scripts.Resolve([' ', 'a', ' ', 0x05D0]);

            Assert.Equal(["Latin", "Latin", "Latin", "Hebrew"], resolved);
        }

        [Fact]
        public void Scripts_ResolveLooked_ResolvesAlreadyLookedUpScripts()
        {
            var resolved = Scripts.ResolveLooked([Scripts.Common, "Latin", Scripts.Inherited]);

            Assert.Equal(["Latin", "Latin", "Latin"], resolved);
        }

        [Fact]
        public void Scripts_RejectNullArguments()
        {
            Assert.Throws<ArgumentNullException>(() => Scripts.Resolve(null!));
            Assert.Throws<ArgumentNullException>(() => Scripts.ResolveLooked(null!));
        }

        [Fact]
        public void OpenTypeTags_MapScriptsAndLanguages()
        {
            Assert.Equal("arab", OpenTypeTags.ForScript("Arabic"));
            Assert.Null(OpenTypeTags.ForScript(Scripts.Common));
            Assert.Null(OpenTypeTags.ForScript(null));
            Assert.Equal("ENG", OpenTypeTags.ForLanguage("en-US"));
            Assert.Null(OpenTypeTags.ForLanguage(null));
            Assert.Null(OpenTypeTags.ForLanguage(""));
        }

        [Fact]
        public void VerticalOrientation_ClassifiesAndAnswersWhetherACharacterStaysUpright()
        {
            Assert.Equal(VerticalOrientationClass.R, VerticalOrientation.Of(new Rune('a')));
            Assert.Equal(VerticalOrientationClass.U, VerticalOrientation.Of(new Rune(0x4E00)));
            Assert.True(VerticalOrientation.IsEffectivelyUpright(new Rune(0x4E00)));
            Assert.False(VerticalOrientation.IsEffectivelyUpright(new Rune('a')));
        }

        [Fact]
        public void DefaultIgnorables_RecognisesInvisibleCharacters()
        {
            Assert.True(DefaultIgnorables.Contains(0x200D));
            Assert.True(DefaultIgnorables.Contains(0xFE0F));
            Assert.False(DefaultIgnorables.Contains('a'));
            Assert.True(DefaultIgnorables.IsVariationSelector(0xFE0F));
            Assert.False(DefaultIgnorables.IsVariationSelector('a'));
        }

        [Fact]
        public void Hyphenator_FindsBreakPointsInASupportedLanguageAndNoneOtherwise()
        {
            var points = Hyphenator.FindBreakPoints("hyphenation", "en-US");

            Assert.NotEmpty(points);
            Assert.All(points, p => Assert.InRange(p, 1, "hyphenation".Length - 1));
            Assert.Empty(Hyphenator.FindBreakPoints("hyphenation", "xx"));
            Assert.Empty(Hyphenator.FindBreakPoints("hyphenation", null));
            Assert.Throws<ArgumentNullException>(() => Hyphenator.FindBreakPoints(null!, "en"));
        }

        [Fact]
        public void Emoji_Resolve_LetsAnExplicitSelectorOverrideTheMode()
        {
            const int Copyright = 0x00A9;

            Assert.Equal(EmojiPresentation.Text, Emoji.Resolve(EmojiMode.Emoji, Copyright, Emoji.TextSelector));
            Assert.Equal(EmojiPresentation.Emoji, Emoji.Resolve(EmojiMode.Text, Copyright, Emoji.EmojiSelector));
            Assert.Equal(EmojiPresentation.Emoji, Emoji.Resolve(EmojiMode.Emoji, Copyright, 0));
            Assert.Equal(EmojiPresentation.NoPreference, Emoji.Resolve(EmojiMode.Normal, Copyright, 0));
        }

        [Fact]
        public void Emoji_ResolveAt_ReadsTheSelectorFromTheText()
        {
            Assert.Equal(EmojiPresentation.Text, Emoji.ResolveAt(EmojiMode.Emoji, "©︎", 0));
            Assert.Equal(EmojiPresentation.Emoji, Emoji.ResolveAt(EmojiMode.Emoji, "x©", 1));
            Assert.Throws<ArgumentNullException>(() => Emoji.ResolveAt(EmojiMode.Emoji, null!, 0));
        }

        [Fact]
        public void Emoji_Predicates_AndSelectors()
        {
            Assert.True(Emoji.IsPresentationParticipant(new Rune(0x00A9)));
            Assert.False(Emoji.IsPresentationParticipant(new Rune('a')));
            Assert.True(Emoji.IsPresentationSelector(new Rune(0xFE0F)));
            Assert.False(Emoji.IsPresentationSelector(new Rune('a')));
            Assert.Equal(0xFE0E, Emoji.SelectorFor(EmojiPresentation.Text));
            Assert.Equal(0xFE0F, Emoji.SelectorFor(EmojiPresentation.Emoji));
            Assert.Equal(0, Emoji.SelectorFor(EmojiPresentation.NoPreference));
        }
    }
}
