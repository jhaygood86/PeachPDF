using PeachPDF.Html.Core.Dom;
using PeachPDF.Tests.TestSupport;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Html.Core
{
    /// <summary>
    /// Integration tests for real UAX#9 bidi resolution driving layout: word-splitting at a bidi level
    /// boundary (<see cref="CssBox.ParseToWords"/>, via <see cref="CssBox.BidiLevels"/>) and per-line L2
    /// reorder/L4 mirroring (<c>CssLayoutEngine.ApplyBidiReordering</c>), following the
    /// <see cref="TestSupport.LayoutHarness"/> pattern: build a real box tree, lay it out, and assert on
    /// the resulting <see cref="CssRectWord"/>s' text/position - not just that layout completes.
    /// </summary>
    public class CssLayoutEngineBidiTests
    {
        [Fact]
        public async Task RtlParagraph_DigitRunWithNoSurroundingWhitespace_SplitsIntoItsOwnWord()
        {
            // "מספר123כאן" has no whitespace/hyphen/CJK boundary anywhere in it - only the bidi level
            // change at the digit run's edges (UAX#9 I2: EN goes up by two at an odd embedding level, so
            // the digits resolve to a different, even level than the surrounding Hebrew) makes this three
            // words instead of one.
            var html = LayoutHarness.Wrap("""<p id="p" dir="rtl" style="width:400pt">מספר123כאן</p>""");

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var p = LayoutHarness.FindById(root, "p");

            Assert.NotNull(p);
            var words = WordsOf(p!);
            Assert.Equal(3, words.Count);

            // The digit run's own characters are never reversed/mirrored (UAX#9 keeps EN left-to-right
            // even inside RTL text) - it is the one word whose text survives unchanged.
            var digits = Assert.Single(words, w => w.Text == "123");

            // L2 reverses the whole line's run order (this line is RTL-outermost - level 1 is present and
            // odd), so the digit run - visually still "between" its two Hebrew neighbors, exactly as a
            // number embedded in Hebrew/Arabic text renders in a real UA - ends up strictly between the
            // other two words' positions, not at either end of the line.
            var others = words.Where(w => w != digits).OrderBy(w => w.Left).ToList();
            Assert.Equal(2, others.Count);
            Assert.True(digits.Left > others[0].Left && digits.Left < others[1].Left,
                $"expected the digit run positioned between its two Hebrew neighbors; digits.Left={digits.Left}, others=[{others[0].Left}, {others[1].Left}]");
        }

        [Fact]
        public async Task Bdo_DirRtl_ReversesPlainLatinTextInLayout()
        {
            // unicode-bidi: bidi-override (the UA stylesheet rule for <bdo>) forces every character's
            // resolved type to match `direction`, regardless of its own real Bidi_Class - so even plain
            // Latin text (ordinarily strong-L, untouched by a plain direction:rtl) gets reordered and
            // mirrored inside a <bdo dir="rtl">, unlike a plain dir="rtl" element around the same text.
            var html = LayoutHarness.Wrap("""<p><bdo id="b" dir="rtl">hello</bdo></p>""");

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var bdo = LayoutHarness.FindById(root, "b");

            Assert.NotNull(bdo);
            var word = Assert.Single(WordsOf(bdo!));
            Assert.Equal("olleh", word.Text);
        }

        [Fact]
        public async Task Bdi_WithNoDirAttribute_DoesNotLeakOwnDirectionIntoSurroundingLtrLine()
        {
            // <bdi> isolates: its own auto-detected RTL directionality (see
            // CascadeAutoDirectionalityTests.Bdi_WithNoDirAttribute_DefaultsToAutoAndIsolates) must not
            // affect how the surrounding LTR sentence's own words are ordered/positioned.
            var html = LayoutHarness.Wrap("""<p id="p">before <bdi id="b">שלום עולם</bdi> after</p>""");

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var p = LayoutHarness.FindById(root, "p");
            var bdi = LayoutHarness.FindById(root, "b");

            Assert.NotNull(p);
            Assert.NotNull(bdi);

            var pWords = WordsOf(p!);
            var before = Assert.Single(pWords, w => w.Text == "before");
            var after = Assert.Single(pWords, w => w.Text == "after");

            // The surrounding LTR sentence keeps its own logical left-to-right word order across the
            // isolated <bdi> - "before" stays left of "after" exactly as authored.
            Assert.True(before.Left < after.Left,
                $"expected the surrounding LTR sentence's own word order to be unaffected by the isolated <bdi>; before.Left={before.Left}, after.Left={after.Left}");

            // The bdi's own two Hebrew words are still reordered relative to EACH OTHER (its own
            // resolution runs independently, isolated from the outer LTR paragraph).
            var bdiWords = WordsOf(bdi!);
            Assert.Equal(2, bdiWords.Count);
            Assert.True(bdiWords[0].Left > bdiWords[1].Left,
                $"expected the isolated <bdi>'s own first logical word to end up rightmost; [0].Left={bdiWords[0].Left}, [1].Left={bdiWords[1].Left}");
        }

        [Fact]
        public async Task RtlParagraph_WordsOfDifferingWidths_DoNotOverlapAfterReordering()
        {
            // Reflecting a run about its own span (not reusing the original per-slot Left of whichever
            // word used to sit there) is what keeps this correct once word widths differ - a version that
            // handed each reordered word the *positional* slot of whatever word it displaced corrupted
            // layout the moment two words in the run had different widths (a slot sized for a narrow word
            // now had to hold a wider one). Every word here is a different length precisely to catch that:
            // a regression would show up as overlapping rectangles, not merely as the wrong left-to-right
            // order (which RtlParagraph_DigitRunWithNoSurroundingWhitespace_SplitsIntoItsOwnWord and the
            // other tests in this file already assert, but none of them check for overlap).
            var html = LayoutHarness.Wrap(
                """<p id="p" dir="rtl" style="width:400pt">שלום עולם זהו טקסט בעברית והוא זורם מימין לשמאל</p>""");

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var p = LayoutHarness.FindById(root, "p");

            Assert.NotNull(p);
            var words = WordsOf(p!).OrderByDescending(w => w.Left).ToList();

            Assert.True(words.Count > 1, "expected more than one word to actually exercise adjacency");

            for (var i = 0; i < words.Count - 1; i++)
            {
                var (leftWord, rightWord) = (words[i + 1], words[i]);
                Assert.True(leftWord.Right <= rightWord.Left + 0.01,
                    $"expected non-overlapping adjacent words in visual (right-to-left) order; " +
                    $"'{rightWord.Text}' [{rightWord.Left:F2}, {rightWord.Right:F2}] overlaps " +
                    $"'{leftWord.Text}' [{leftWord.Left:F2}, {leftWord.Right:F2}]");
            }
        }

        [Fact]
        public async Task MultipleRunBoundaries_KeepUniformInterWordSpacing()
        {
            // A run's own trailing gap to whatever comes after it must never take part in that run's own
            // reflection: an earlier version folded a run's trailing gap into its own width, so reflecting
            // an RTL run moved that gap onto the run's *leading* edge instead of leaving it trailing -
            // doubling the gap on one side of each RTL run and erasing it on the other. Five runs (LTR,
            // RTL, LTR, RTL, LTR) with a plain space between every pair of words is enough to expose that:
            // every one of the four gaps below should come out equal, not alternate large/zero.
            var html = LayoutHarness.Wrap(
                """<p id="p" dir="ltr" style="width:400pt; font-family: Arial">AB שלום CD עולם EF</p>""");

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var p = LayoutHarness.FindById(root, "p");

            Assert.NotNull(p);
            var words = WordsOf(p!).Where(w => !w.IsSpaces).OrderBy(w => w.Left).ToList();
            Assert.Equal(5, words.Count);

            var gaps = new List<double>();
            for (var i = 0; i < words.Count - 1; i++)
            {
                gaps.Add(words[i + 1].Left - words[i].Right);
            }

            Assert.All(gaps, gap => Assert.Equal(gaps[0], gap, 1));
        }

        private static List<CssRectWord> WordsOf(CssBox box) =>
            LayoutHarness.Descendants(box)
                .SelectMany(b => b.Words.OfType<CssRectWord>())
                .Where(w => w.Text != "\n")
                .ToList();
    }
}
