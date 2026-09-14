using PeachPDF.CSS;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Parse;
using PeachPDF.Tests.TestSupport;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Coverage for CSS Overflow 4's <c>line-clamp</c>: parsing/storage, the layout-time line-count
    /// cutoff in <c>CssLayoutEngine.CreateLineBoxes</c>'s wrap loop (<c>TryApplyLineClamp</c>), and the
    /// generated ellipsis word it appends to the last visible line. Per this repo's convention, a token
    /// being accepted is not proof the feature actually changes rendered output - every non-parse test
    /// here lays real content out and inspects the resulting <see cref="CssBox.LineBoxes"/>/<see cref="CssRect"/>,
    /// not just that layout completes.
    /// </summary>
    public class LineClampIntegrationTests
    {
        // Each word is wider than the 1px container, so ordinary word-wrapping (no overflow-wrap
        // splitting is declared) puts exactly one whole word per line - giving line count == word count
        // as a precise, deterministic control over how many lines real content would produce.
        private const string FiveWords = "AAAAA BBBBB CCCCC DDDDD EEEEE";

        // FragmentEmitter.BuildDraft (the layer paint actually consumes - see CLAUDE.md's fragment-tree
        // architecture note) walks a box's own CssBox.Words, not CssLineBox.Words - so a test asserting
        // only box.LineBoxes[^1].Words (as every other test in this file does) cannot by itself prove
        // the ellipsis reaches paint. This is exactly the class of bug the "a token being accepted is
        // not proof a feature renders correctly" convention exists for: the layout-side assertions all
        // passed while the ellipsis was reaching CssLineBox.Words but not the owning CssBox's own Words
        // list, so FragmentEmitter never saw it and nothing painted at all - caught only by inspecting
        // the actual BoxFragment.Words a real showcase render produces.
        [Fact]
        public async Task LineClamp_EllipsisReachesTheFragmentTreePaintConsumes()
        {
            // Wide enough that both a real word and the ellipsis can coexist on the last visible line -
            // the point of this test is confirming the ellipsis actually reaches paint (see the comment
            // above), not exercising the word-popping fit logic another test already covers.
            var html = LayoutHarness.Wrap(
                "<p id='p' style='width:120px;font:16px monospace;margin:0;line-clamp:2'>AAAAA BBBBB CCCCC DDDDD EEEEE</p>");
            var (root, container) = await LayoutHarness.LayoutAsync(html);
            var owner = LayoutHarness.Descendants(root).FirstOrDefault(x => x.Words.Count > 0);
            Assert.NotNull(owner);

            var fragment = FragmentPaintHarness.FragmentOf(container, owner!);
            Assert.Contains(fragment.Words, w => w.Word.Text == "…");

            // The block's very first word always survives (line 1 is always fully visible), and the
            // last word of the whole paragraph never does (it's well past a 2-line clamp regardless of
            // exactly where each of the five words happens to wrap) - together these confirm the
            // ellipsis is standing in for genuinely truncated content, not just present alongside all of it.
            Assert.Contains(fragment.Words, w => w.Word.Text == "AAAAA");
            Assert.DoesNotContain(fragment.Words, w => w.Word.Text == "EEEEE");
        }

        // ─── parsing/storage ────────────────────────────────────────────────────

        [Fact]
        public async Task LineClamp_DefaultsToNone()
        {
            var box = await FindByIdAsync("<p id='p'>text</p>");
            Assert.False(box.LineClamp.Value.IsValue);
            Assert.Equal(NoneKeyword.None, box.LineClamp.Value.Keyword);
        }

        [Fact]
        public async Task LineClamp_ParsesPositiveInteger()
        {
            var box = await FindByIdAsync("<p id='p' style='line-clamp:3'>text</p>");
            Assert.True(box.LineClamp.Value.IsValue);
            Assert.Equal(3, box.LineClamp.Value.Value);
        }

        [Fact]
        public async Task BlockEllipsis_DefaultsToAuto()
        {
            var box = await FindByIdAsync("<p id='p'>text</p>");
            Assert.Equal("auto", box.BlockEllipsis, ignoreCase: true);
        }

        [Fact]
        public async Task BlockEllipsis_ParsesCustomStringAndNone()
        {
            var custom = await FindByIdAsync("<p id='p' style='block-ellipsis:\"[more]\"'>text</p>");
            Assert.True(CssValueParser.TryParseSingleString(custom.BlockEllipsis, out var customText));
            Assert.Equal("[more]", customText);

            var none = await FindByIdAsync("<p id='p' style='block-ellipsis:none'>text</p>");
            Assert.Equal("none", none.BlockEllipsis, ignoreCase: true);

            var auto = await FindByIdAsync("<p id='p' style='block-ellipsis:auto'>text</p>");
            Assert.Equal("auto", auto.BlockEllipsis, ignoreCase: true);
        }

        [Fact]
        public async Task BlockEllipsis_RejectsAnUnquotedValue()
        {
            // An invalid declaration is dropped entirely by real CSS cascade parsing - the property
            // keeps its own initial value rather than storing the bad text.
            var box = await FindByIdAsync("<p id='p' style='block-ellipsis:more'>text</p>");
            Assert.Equal("auto", box.BlockEllipsis, ignoreCase: true);
        }

        [Fact]
        public async Task BlockEllipsis_Inherits()
        {
            // Unlike line-clamp's own longhands (max-lines/continue - see LineClamp_DoesNotInherit
            // above), block-ellipsis itself IS inherited per the real CSS Overflow 4 property table - a
            // parent's custom marker applies to any descendant's own line-clamp without needing to be
            // redeclared on every clamped element.
            var html = LayoutHarness.Wrap(
                "<div id='parent' style='block-ellipsis:\"[more]\"'><p id='child'>text</p></div>");
            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var child = LayoutHarness.FindById(root, "child")!;

            Assert.True(CssValueParser.TryParseSingleString(child.BlockEllipsis, out var inheritedText));
            Assert.Equal("[more]", inheritedText);
        }

        [Fact]
        public async Task BlockEllipsis_OwnDeclarationOverridesTheInheritedOne()
        {
            var html = LayoutHarness.Wrap(
                "<div id='parent' style='block-ellipsis:\"[more]\"'>" +
                "<p id='child' style='block-ellipsis:none'>text</p></div>");
            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var child = LayoutHarness.FindById(root, "child")!;

            Assert.Equal("none", child.BlockEllipsis, ignoreCase: true);
        }

        // ─── layout: line-count cutoff ──────────────────────────────────────────

        [Fact]
        public async Task LineClamp_None_LaysOutEveryLine()
        {
            var box = await FindByIdAsync(NarrowParagraph(FiveWords, lineClamp: null));
            Assert.Equal(5, box.LineBoxes.Count);
        }

        [Fact]
        public async Task LineClamp_LimitsVisibleLineCount()
        {
            var box = await FindByIdAsync(NarrowParagraph(FiveWords, lineClamp: 2));
            Assert.Equal(2, box.LineBoxes.Count);
        }

        [Fact]
        public async Task LineClamp_AppendsEllipsisToLastVisibleLine()
        {
            var box = await FindByIdAsync(NarrowParagraph(FiveWords, lineClamp: 2));

            var lastLine = box.LineBoxes[^1];
            Assert.Contains(lastLine.Words, w => w.Text == "…");

            // Regression: the line's own real content ("BBBBB", line 2 of the fixture) must survive
            // alongside the ellipsis. In this 1px-wide fixture the word already overflows the container
            // on its own, regardless of the ellipsis - a fit-driven pop loop with no floor would keep
            // popping until the line held nothing but the generated ellipsis.
            Assert.Contains(lastLine.Words, w => w.Text == "BBBBB");
        }

        [Fact]
        public async Task LineClamp_CustomBlockEllipsis_AppendsCustomMarkerInsteadOfDefault()
        {
            var box = await FindByIdAsync(NarrowParagraph(FiveWords, lineClamp: 2, blockEllipsis: "\"[more]\""));

            var lastLine = box.LineBoxes[^1];
            Assert.DoesNotContain(lastLine.Words, w => w.Text == "…");
            Assert.Contains(lastLine.Words, w => w.Text == "[more]");
        }

        [Fact]
        public async Task LineClamp_BlockEllipsisNone_StillClampsButAddsNoMarkerAtAll()
        {
            var box = await FindByIdAsync(NarrowParagraph(FiveWords, lineClamp: 2, blockEllipsis: "none"));

            // Still cuts off after the declared limit...
            Assert.Equal(2, box.LineBoxes.Count);

            // ...but with nothing appended in place of the truncated content, and the last visible
            // line's own real words left untouched (none popped to make room for a marker that was
            // never going to be added).
            var words = box.LineBoxes.SelectMany(l => l.Words).ToList();
            Assert.DoesNotContain(words, w => w.Text == "…");
            Assert.DoesNotContain(words, w => w.Text == "[more]");
            Assert.Contains(box.LineBoxes[^1].Words, w => w.Text == "BBBBB");
        }

        [Fact]
        public async Task LineClamp_PopsOnlyAsManyTrailingWordsAsNeededToFitTheEllipsis()
        {
            var html = await BuildFixtureNeedingExactlyOnePopAsync();
            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var box = LayoutHarness.FindById(root, "p")!;

            Assert.Single(box.LineBoxes);
            var line = box.LineBoxes[0];
            Assert.Contains(line.Words, w => w.Text == "AAAAA");
            Assert.DoesNotContain(line.Words, w => w.Text == "BBBBB");
            Assert.Contains(line.Words, w => w.Text == "…");

            // The popped word must be gone from its OWNER box's own Words list too, not just the line's -
            // FragmentEmitter (the layer paint actually consumes) walks CssBox.Words directly. The words'
            // real owner is an anonymous box the parser wraps bare block text in, not "p" itself - a box
            // holding both child boxes and its own words is invalid here (see the
            // dom-a-box-must-never-hold-both-its-own-words-and-child-boxes invariant), and "p" has none of
            // its own text as a direct child once that wrapping happens.
            var owner = LayoutHarness.Descendants(root).First(b => b.Words.Any(w => w.Text == "AAAAA"));
            Assert.DoesNotContain(owner.Words, w => w.Text == "BBBBB");
            Assert.Contains(owner.Words, w => w.Text == "AAAAA");
        }

        [Fact]
        public async Task LineClamp_RepeatedLayoutRestoresAPoppedWordBeforeReclamping()
        {
            // Regression test for the LineClampPoppedWords side of the same multi-pass bug
            // LineClamp_RepeatedLayoutOverSameTreeStaysStable covers for the ellipsis: a fixture that
            // actually needs to pop a word (not just the "line already has only one word" case that
            // fixture's 1px-wide container always produces) must still come out identical on every pass
            // - BBBBB restored, then popped again fresh, not left missing (understated word count) or
            // duplicated (a stale copy alongside a freshly-popped one).
            var html = await BuildFixtureNeedingExactlyOnePopAsync();

            var results = await LayoutHarness.LayoutRepeatedlyAsync(html, passes: 3, (root, _) =>
            {
                var owner = LayoutHarness.Descendants(root).First(b => b.Words.Any(w => w.Text == "AAAAA"));
                return (
                    OwnerWordCount: owner.Words.Count,
                    BbbbbCount: owner.Words.Count(w => w.Text == "BBBBB"),
                    CccccCount: owner.Words.Count(w => w.Text == "CCCCC"),
                    EllipsisCount: owner.Words.Count(w => w.Text == "…"));
            });

            foreach (var result in results)
            {
                // BBBBB was popped (removed, not merely skipped) to make room for the ellipsis, and
                // CCCCC was never reached at all (clamped away before its own line ever opened) but
                // still belongs to the owner's full parsed word list either way - so a stable pass sees
                // exactly [AAAAA, CCCCC, ellipsis], never BBBBB and never a duplicate ellipsis.
                Assert.Equal(3, result.OwnerWordCount);
                Assert.Equal(0, result.BbbbbCount);
                Assert.Equal(1, result.CccccCount);
                Assert.Equal(1, result.EllipsisCount);
            }
        }

        [Fact]
        public async Task LineClamp_EarlierLines_CarryNoEllipsis()
        {
            var box = await FindByIdAsync(NarrowParagraph(FiveWords, lineClamp: 2));

            var firstLine = box.LineBoxes[0];
            Assert.DoesNotContain(firstLine.Words, w => w.Text == "…");
        }

        [Fact]
        public async Task LineClamp_ContentFitsWithinLimit_NoEllipsisAdded()
        {
            // Only two words, well inside a limit of 5 - line-clamp never has anything to truncate.
            var box = await FindByIdAsync(NarrowParagraph("AAAAA BBBBB", lineClamp: 5));

            Assert.Equal(2, box.LineBoxes.Count);
            Assert.DoesNotContain(box.LineBoxes.SelectMany(l => l.Words), w => w.Text == "…");
        }

        [Fact]
        public async Task LineClamp_ShrinksActualBottomToVisibleLinesOnly()
        {
            var unclamped = await FindByIdAsync(NarrowParagraph(FiveWords, lineClamp: null));
            var clamped = await FindByIdAsync(NarrowParagraph(FiveWords, lineClamp: 2));

            // Exact, not just "shorter" (which an inflated - but still under 5-lines-worth - height
            // would also satisfy): a clamped block's own height is its content box up to the bottom of
            // its last visible line, plus whatever fixed trailing offset (padding/leading rounding) the
            // block always adds after its own last line regardless of how many lines that is - derived
            // here from the unclamped run's own geometry rather than assumed, since line pitch plus that
            // trailing offset does not simply scale line count against total height proportionally.
            var expectedHeight = unclamped.LineBoxes[1].LineBottom +
                                  (unclamped.ActualBottom - unclamped.LineBoxes[^1].LineBottom);
            Assert.Equal(expectedHeight, clamped.ActualBottom, precision: 6);
        }

        [Fact]
        public async Task LineClamp_DoesNotInflateHeightForTheNeverRenderedNextWord()
        {
            // Regression: the incoming (about-to-wrap) word's speculative line-height growth
            // (GrowLineToItsExtent, applied before the wrap-vs-clamp decision is even made) must be
            // undone before a clamped stop returns, exactly as it already is for an ordinary wrap - a
            // clamped block's height must not depend on how tall the content it never renders would
            // have been.
            var smallNextWord = await FindByIdAsync(LayoutHarness.Wrap(
                "<p id='p' style='width:1px;margin:0;line-clamp:1'>" +
                "<span style='font:16px monospace'>AAAAA</span> <span style='font:16px monospace'>BBBBB</span></p>"));
            var largeNextWord = await FindByIdAsync(LayoutHarness.Wrap(
                "<p id='p' style='width:1px;margin:0;line-clamp:1'>" +
                "<span style='font:16px monospace'>AAAAA</span> <span style='font:60px monospace'>BBBBB</span></p>"));

            Assert.Equal(smallNextWord.ActualBottom, largeNextWord.ActualBottom, precision: 3);
        }

        [Fact]
        public async Task LineClamp_LimitLargerThanContent_ProducesEveryLineAndNoEllipsis()
        {
            var box = await FindByIdAsync(NarrowParagraph(FiveWords, lineClamp: 10));

            Assert.Equal(5, box.LineBoxes.Count);
            Assert.DoesNotContain(box.LineBoxes.SelectMany(l => l.Words), w => w.Text == "…");
        }

        [Fact]
        public async Task LineClamp_DoesNotInherit()
        {
            // CSS Overflow 4 marks both of line-clamp's longhands (max-lines, continue) Inherited: no -
            // regression test for a bug where the property was added to the otherwise-100%-inherited
            // TextArea's whole-area adoption in CssBox.InheritStyle without also being added to that
            // method's own non-inherited-properties restore list, so it silently inherited anyway (a
            // <div style="line-clamp:2"> clamping every child paragraph to 2 lines each, rather than
            // clamping only the div's own direct line boxes).
            var html = LayoutHarness.Wrap(
                "<div id='parent' style='line-clamp:2'><p id='child'>text</p></div>");
            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var child = LayoutHarness.FindById(root, "child")!;

            Assert.False(child.LineClamp.Value.IsValue);
            Assert.Equal(NoneKeyword.None, child.LineClamp.Value.Keyword);
        }

        [Fact]
        public async Task LineClamp_RepeatedLayoutOverSameTreeStaysStable()
        {
            // A real layout pipeline can re-run CreateLineBoxes for the same box more than once over an
            // unchanged tree (an unrestricted-width measure pass, a variable-page-width reflow, a
            // multi-column fill attempt, ...). Regression test for a bug where TryApplyLineClamp's
            // CssBox.Words mutations (popped words removed, an ellipsis appended) were never undone
            // between passes: a second pass saw the already-shortened list and appended a second
            // ellipsis on top of it, a third pass a third, and so on without bound.
            var html = NarrowParagraph(FiveWords, lineClamp: 2);

            var results = await LayoutHarness.LayoutRepeatedlyAsync(html, passes: 3, (root, _) =>
            {
                var box = LayoutHarness.FindById(root, "p")!;
                var words = box.LineBoxes.SelectMany(l => l.Words).ToList();
                return (
                    EllipsisCount: words.Count(w => w.Text == "…"),
                    WordCount: words.Count,
                    LineCount: box.LineBoxes.Count);
            });

            foreach (var result in results)
            {
                Assert.Equal(1, result.EllipsisCount);
                Assert.Equal(2, result.LineCount);
            }

            Assert.All(results, r => Assert.Equal(results[0].WordCount, r.WordCount));
        }

        // ─── helpers ────────────────────────────────────────────────────────────

        private static string NarrowParagraph(string text, int? lineClamp, string? blockEllipsis = null) =>
            LayoutHarness.Wrap(
                $"<p id='p' style='width:1px;font:16px monospace;margin:0" +
                (lineClamp is { } n ? $";line-clamp:{n}" : "") +
                (blockEllipsis is not null ? $";block-ellipsis:{blockEllipsis}" : "") +
                $"'>{text}</p>");

        private static async Task<CssBox> FindByIdAsync(string html)
        {
            var (root, _) = await LayoutHarness.LayoutAsync(html);
            return LayoutHarness.FindById(root, "p")!;
        }

        /// <summary>
        /// A <c>line-clamp:1</c> fixture ("AAAAA BBBBB CCCCC") sized to need popping exactly one
        /// trailing word: the container is measured to fit "AAAAA BBBBB" with zero width to spare, plus
        /// half the ellipsis glyph's own measured width (comfortably more than floating-point fit/
        /// rounding fuzz, comfortably less than the full ellipsis) - enough that both words are placed
        /// on the line, but not enough left over for the ellipsis beside them, so exactly one pop (of
        /// BBBBB, not AAAAA too) is needed to make room for it.
        /// </summary>
        private static async Task<string> BuildFixtureNeedingExactlyOnePopAsync()
        {
            const string style = "font:16px monospace;margin:0";
            var (naturalRoot, _) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap($"<p id='m' style='{style}'>AAAAA BBBBB</p>"));
            var naturalLine = LayoutHarness.FindById(naturalRoot, "m")!.LineBoxes[0];
            // A word's own Left/Right are absolute document positions (including the page's own left
            // margin), not a size - subtracting the line's own content-left turns it into the size a
            // CSS width declaration actually needs.
            var naturalWidth = naturalLine.Words[^1].Right - naturalLine.ContentLeft;
            var (ellipsisRoot, _) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap($"<p id='e' style='{style}'>…</p>"));
            var ellipsisWidth = LayoutHarness.FindById(ellipsisRoot, "e")!.LineBoxes[0].Words[0].Width;

            return LayoutHarness.Wrap(
                $"<p id='p' style='{style};width:{naturalWidth + ellipsisWidth / 2}pt;line-clamp:1'>AAAAA BBBBB CCCCC</p>");
        }
    }
}
