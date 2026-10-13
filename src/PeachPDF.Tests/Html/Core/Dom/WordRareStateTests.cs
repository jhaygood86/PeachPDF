using PeachPDF.Html.Core.Dom;
using PeachPDF.Tests.TestSupport;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Html.Core.Dom
{
    /// <summary>
    /// A word (<see cref="CssRectWord"/>) keeps the state only an unusual word has - hyphenation pieces, overflow-wrap pieces,
    /// first-line and scaled-font variants - in <see cref="WordRareState"/>, allocated for the first value that is not the default.
    /// </summary>
    public class WordRareStateTests
    {
        private static CssRectWord NewWord() => new(new CssBox(null, null), "word", false, false);

        [Fact]
        public void ANewWord_ReadsEveryDefault_WithoutAllocating()
        {
            var word = NewWord();

            Assert.Null(word.FirstLineStyle);
            Assert.Null(word.FirstLineText);
            Assert.Null(word.OverflowWrapMinWidth);
            Assert.Null(word.HyphenationCandidates);
            Assert.Null(word.PreSplitWord);
            Assert.Null(word.HyphenationSuffix);
            Assert.Null(word.HyphenationPrefix);
            Assert.Null(word.PreOverflowWrapWord);
            Assert.Null(word.OverflowWrapSuffix);
            Assert.Null(word.OverflowWrapPrefix);
            Assert.Equal(1.0, word.FontSizeScale);
            Assert.Equal(ScaledFontKind.None, word.ScaledFontKind);
            Assert.False(word.UsesPerCodepointFont);
            Assert.False(word.IsUprightOrientation);
            Assert.False(word.SuppressWrapBefore);
            Assert.False(word.HasRareState);
        }

        [Fact]
        public void WritingADefault_DoesNotAllocate()
        {
            var word = NewWord();

            word.FontSizeScale = 1.0;
            word.ScaledFontKind = ScaledFontKind.None;
            word.UsesPerCodepointFont = false;
            word.HyphenationCandidates = null;
            word.PreSplitWord = null;

            Assert.False(word.HasRareState);
        }

        [Fact]
        public void AValueWritten_IsReadBack_AndAnotherMemberKeepsItsDefault()
        {
            var word = NewWord();
            var suffix = NewWord();

            word.HyphenationSuffix = suffix;
            word.OverflowWrapMinWidth = 12.5;
            word.FontSizeScale = 0.8;
            word.IsUprightOrientation = true;
            word.HyphenationCandidates = [2, 4];

            Assert.True(word.HasRareState);
            Assert.Same(suffix, word.HyphenationSuffix);
            Assert.Equal(12.5, word.OverflowWrapMinWidth);
            Assert.Equal(0.8, word.FontSizeScale);
            Assert.True(word.IsUprightOrientation);
            Assert.Equal([2, 4], word.HyphenationCandidates);
            Assert.Null(word.HyphenationPrefix);
            Assert.False(word.SuppressWrapBefore);

            word.FontSizeScale = 1.0;
            Assert.Equal(1.0, word.FontSizeScale);
        }

        [Fact]
        public async Task OrdinaryLatinWords_AfterLayout_HoldNoRareState()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<p id='p'>An ordinary paragraph of plain words, with <b>bold</b> and <i>italic</i> runs, long enough to wrap over several lines.</p>"));

            var words = LayoutHarness.FindById(root, "p")!.Boxes.SelectMany(b => b.Words).ToList();

            Assert.NotEmpty(words);
            Assert.All(words, word => Assert.False(word.HasRareState));
        }

        [Fact]
        public async Task HyphenatedWords_GetTheirPieces()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='d' lang='en-us' style='width: 60pt; hyphens: auto; font-size: 12pt'>incomprehensibilities extraordinarily</div>"));

            var words = LayoutHarness.FindById(root, "d")!.Boxes.SelectMany(b => b.Words).ToList();

            Assert.Contains(words, word => word.HasRareState);
        }
    }
}
