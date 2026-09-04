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
            // unicode-bidi: isolate-override (the UA stylesheet rule for <bdo>) forces every character's
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
        public async Task DirRtlSpan_InLtrParagraph_IsolatesFromSurroundingDigitsAndNeutrals()
        {
            // The UA stylesheet's [dir=rtl] rule is unicode-bidi: isolate (not the legacy embed) per the
            // current HTML Standard - the RTL span must be opaque to the surrounding LTR paragraph's own
            // resolution, so "1" and "2" keep the paragraph's own left-to-right document order around it
            // instead of the span's internal RTL levels leaking into how the neighboring digits/neutrals
            // resolve (see CascadeAutoDirectionalityTests.DirRtl_Explicit_SetsIsolateNotOverride for the
            // cascade-level assertion that the property itself is isolate, not embed).
            var html = LayoutHarness.Wrap("""<p id="p">1 <span dir="rtl">עברית</span> 2</p>""");

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var p = LayoutHarness.FindById(root, "p");

            Assert.NotNull(p);
            var words = WordsOf(p!);
            var one = Assert.Single(words, w => w.Text == "1");
            var two = Assert.Single(words, w => w.Text == "2");
            // Same UAX#9 L2/L4 visual mirroring as the <bdo> tests above (see Bdo_DirRtl_ReversesPlainLatinTextInLayout)
            // reverses the isolated run's own character order within its own scope - "עברית" becomes "תירבע".
            var hebrew = Assert.Single(words, w => w.Text == "תירבע");

            Assert.True(one.Left < hebrew.Left && hebrew.Left < two.Left,
                $"expected the paragraph's own document order (1, span, 2) preserved around the isolated " +
                $"RTL span; one.Left={one.Left}, hebrew.Left={hebrew.Left}, two.Left={two.Left}");
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

        [Fact]
        public async Task Bdo_LaidOutMoreThanOnce_StaysMirrored()
        {
            // Mirroring is an involution, so it must never be derived from a word's own current (possibly
            // already-mirrored) Text - HtmlContainerInt.PerformLayout can lay the same box tree out more
            // than once (its variable-page-width reflow re-runs LayoutDocument up to several times per
            // call, re-deriving line boxes and re-applying bidi reordering each time), and mirroring an
            // already-mirrored word right back would silently restore the pre-mirror text on every second
            // pass. LayoutRepeatedlyAsync models exactly that: the same CssRectWord objects laid out
            // repeatedly, not a fresh parse each time.
            var html = LayoutHarness.Wrap("""<p><bdo id="b" dir="rtl">hello</bdo></p>""");

            var results = await LayoutHarness.LayoutRepeatedlyAsync(html, 3, (root, _) =>
            {
                var bdo = LayoutHarness.FindById(root, "b")!;
                return LayoutHarness.Descendants(bdo).SelectMany(b => b.Words).First().Text;
            });

            Assert.All(results, text => Assert.Equal("olleh", text));
        }

        [Fact]
        public async Task UnicodeBidiPlaintext_OnBlockWithExplicitLtrDirection_RedirectsFromLeadingStrongRtlCharacter()
        {
            // CSS Writing Modes 4 §2.2: unicode-bidi: plaintext re-derives the paragraph's own base
            // direction from the first strong character in its content (UAX#9 P2/P3), regardless of the
            // computed `direction` property - dir="ltr" here must NOT win. "שלום עולם" starts with a
            // strong-R character, so the detected base direction is RTL and the two words reorder right-
            // to-left, exactly as they would under a real dir="rtl" (see
            // RtlParagraph_DigitRunWithNoSurroundingWhitespace_SplitsIntoItsOwnWord's plain-RTL sibling
            // case) rather than staying in the plain LTR order dir="ltr" alone would keep.
            var html = LayoutHarness.Wrap(
                """<p id="p" dir="ltr" style="unicode-bidi:plaintext; width:400pt">שלום עולם</p>""");

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var p = LayoutHarness.FindById(root, "p");

            Assert.NotNull(p);
            var words = WordsOf(p!);
            Assert.Equal(2, words.Count);

            // Same L2 per-word character reversal into visual order the plain dir="rtl" tests above show
            // (e.g. DirRtlSpan_InLtrParagraph_IsolatesFromSurroundingDigitsAndNeutrals's "עברית" ->
            // "תירבע") - "שלום" -> "םולש", "עולם" -> "םלוע".
            var shalom = Assert.Single(words, w => w.Text == "םולש");
            var olam = Assert.Single(words, w => w.Text == "םלוע");

            // Logical order is "שלום" then "עולם" - a detected RTL base direction reorders them so the
            // first logical word ends up rightmost, the same visual pattern the plain dir="rtl" tests
            // above assert (e.g. RtlParagraph_WordsOfDifferingWidths_DoNotOverlapAfterReordering).
            Assert.True(shalom.Left > olam.Left,
                $"expected plaintext's own first-strong-character detection to lay this out RTL despite dir=\"ltr\"; שלום.Left={shalom.Left}, עולם.Left={olam.Left}");
        }

        [Fact]
        public async Task UnicodeBidiPlaintext_OnInlineSpan_DetectsOwnDirectionWithoutLeakingIntoSurroundingParagraph()
        {
            // An inline unicode-bidi: plaintext span establishes its own isolated first-strong-detected
            // run (mapped to a synthetic FSI push - see CssUnicodeBidiMapping.MapToPushes) inside the
            // surrounding LTR paragraph, the same isolation shape as Bdi_WithNoDirAttribute_... above, but
            // driven by content detection rather than an explicit dir="rtl".
            var html = LayoutHarness.Wrap(
                """<p id="p">before <span id="s" style="unicode-bidi:plaintext">שלום עולם</span> after</p>""");

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var p = LayoutHarness.FindById(root, "p");
            var span = LayoutHarness.FindById(root, "s");

            Assert.NotNull(p);
            Assert.NotNull(span);

            var pWords = WordsOf(p!);
            var before = Assert.Single(pWords, w => w.Text == "before");
            var after = Assert.Single(pWords, w => w.Text == "after");
            Assert.True(before.Left < after.Left,
                $"expected the surrounding LTR sentence's own word order to be unaffected by the isolated plaintext span; before.Left={before.Left}, after.Left={after.Left}");

            var spanWords = WordsOf(span!);
            Assert.Equal(2, spanWords.Count);
            var shalom = Assert.Single(spanWords, w => w.Text == "םולש");
            var olam = Assert.Single(spanWords, w => w.Text == "םלוע");
            Assert.True(shalom.Left > olam.Left,
                $"expected the plaintext span's own content to be detected as RTL and reordered; שלום.Left={shalom.Left}, עולם.Left={olam.Left}");
        }

        [Fact]
        public async Task GeneratedContent_OnBeforeBox_ParticipatesInBidiResolution()
        {
            // Issue #551: CssContentEngine.ApplyContent sets Text directly on a ::before/::after
            // pseudo-element's own generated-content box, rather than on a further anonymous child
            // text box the way ordinary DOM text always is - and (per DomParser.CorrectTextBoxes)
            // does so only *after* CssBidiParagraphResolver.AssignBidiLevels's own whole-tree walk has
            // already completed (the box exists by then, empty, from selector-match time, but its
            // `content` isn't resolved onto it until CorrectTextBoxes reaches it). Without a bidi pass
            // re-run once the box's own text actually exists, ParseToWords falls back to a uniform
            // Direction-derived level and fully reverses/mirrors even plain Latin text and digits,
            // which UAX#9 (I1/I2) says must stay left-to-right inside RTL content.
            var html = LayoutHarness.Wrap("""
                <style>p::before { content: "abc 123 "; }</style>
                <p id="p" dir="rtl">שלום</p>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var p = LayoutHarness.FindById(root, "p");
            Assert.NotNull(p);

            var words = WordsOf(p!);
            Assert.Contains(words, w => w.Text == "abc");
            Assert.Contains(words, w => w.Text == "123");
            Assert.DoesNotContain(words, w => w.Text == "cba");
            Assert.DoesNotContain(words, w => w.Text == "321");
        }

        private static List<CssRectWord> WordsOf(CssBox box) =>
            LayoutHarness.Descendants(box)
                .SelectMany(b => b.Words.OfType<CssRectWord>())
                .Where(w => w.Text != "\n")
                .ToList();
    }
}
