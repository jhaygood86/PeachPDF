using PeachPDF.Html.Core.Dom;
using PeachPDF.Tests.TestSupport;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// An atomic inline-level box reserves no space of its own after itself: per
    /// <see href="https://www.w3.org/TR/css-text-3/#white-space-phase-1">css-text-3 §4.1.1</see> only
    /// white space in the source produces an advance between two adjacent inline-level boxes, so
    /// <c>&lt;img&gt;text</c> is exactly as contiguous as
    /// <c>&lt;span&gt;Y&lt;/span&gt;&lt;span&gt;X&lt;/span&gt;</c>.
    ///
    /// <c>CssRect.ActualWordSpacing</c> used to add one unconditional word space after every
    /// <c>CssRect.IsImage</c> word on top of whatever a real trailing space already contributed, so an
    /// image or inline <c>&lt;svg&gt;</c> pushed what followed it a full space to the right, and one
    /// followed by actual white space got two spaces' worth (issue #1011). The gap is measured against
    /// the all-text control in the same document, at the same font, so it states the real invariant
    /// ("the image behaves like a glyph run") rather than a font-dependent constant.
    /// </summary>
    public class AtomicInlineSpacingIntegrationTests
    {
        /// <summary>A 1x1 PNG - the content never matters here, only that the image decodes.</summary>
        private const string Png =
            "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8"
            + "z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==";

        private static readonly string Image =
            $"<img src='{Png}' style='width:13px;height:13px' />";

        private const string Svg = "<svg width='13' height='13'></svg>";

        [Theory]
        [InlineData("image")]
        [InlineData("svg")]
        public async Task AtomicInline_FollowedImmediatelyByText_ReservesNoGap(string kind)
        {
            var words = await WordsAsync($"{Replaced(kind)}text");

            Assert.Equal(2, words.Count);
            Assert.True(words[0].IsImage);
            Assert.Equal(ReplacedWidthPt, words[0].Width, 0.01);
            Assert.Equal(0, words[1].Left - words[0].Right, 0.01);
        }

        [Theory]
        [InlineData("image")]
        [InlineData("svg")]
        public async Task AtomicInline_FollowedByOneSpace_ReservesExactlyOneSpace(string kind)
        {
            // The control is two adjacent text runs separated by the same single space, so "one
            // space" is whatever this font actually measures rather than a hard-coded width.
            var words = await WordsAsync($"{Replaced(kind)} text");
            var oneSpace = await GapAfterFirstWordAsync("<span>Y</span> text");

            Assert.Equal(2, words.Count);
            Assert.Equal(ReplacedWidthPt, words[0].Width, 0.01);

            var gap = words[1].Left - words[0].Right;
            Assert.Equal(oneSpace, gap, 0.01);
            Assert.True(gap > 0, $"A source space must still produce a real gap, but it was {gap}");
        }

        [Fact]
        public async Task AdjacentTextRuns_ReserveNoGap_Control()
        {
            // The control the two cases above are compared against: nothing about an atomic inline is
            // involved, so a regression here would mean the harness, not the fix, had moved.
            var gap = await GapAfterFirstWordAsync("<span>Y</span><span>X</span>");

            Assert.Equal(0, gap, 0.01);
        }

        [Theory]
        [InlineData("image")]
        [InlineData("svg")]
        public async Task TwoAdjacentAtomicInlines_SitFlush(string kind)
        {
            // Neither one reserves anything, so the second starts exactly where the first ends - the
            // case an icon strip (<img><img><img>) actually hits.
            var replaced = Replaced(kind);
            var words = await WordsAsync($"{replaced}{replaced}");

            Assert.Equal(2, words.Count);
            Assert.All(words, word => Assert.True(word.IsImage));
            Assert.All(words, word => Assert.Equal(ReplacedWidthPt, word.Width, 0.01));
            Assert.Equal(words[0].Right, words[1].Left, 0.01);
        }

        [Theory]
        [InlineData("image")]
        [InlineData("svg")]
        public async Task AtomicInline_DoesNotWidenTheMaxContentWidthOfItsBlock(string kind)
        {
            // The phantom space lived in ActualWordSpacing, which CssBox.GetMinMaxWidth also sums via
            // CssRect.FullWidth - so a block holding atomic inlines measured a whole space per inline
            // wider than it draws, and every shrink-to-fit consumer of that measurement (a table
            // column, a float, an inline-block) inherited the error. Two of them, because the old
            // code discounted the *last* word's spacing at the end of GetMinMaxSumWords (the
            // "remove the last word padding" line, deleted with the phantom term it existed to
            // cancel) - so a single-inline block would have measured right by accident and this
            // would not have been a guard at all.
            var replaced = Replaced(kind);
            double maxWidth = 0;
            double wordWidthSum = 0;

            await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap($"<div id='d' style='font:16px monospace'>{replaced}{replaced}</div>"),
                after: (root, _container, graphics) =>
                {
                    var block = LayoutHarness.FindById(root, "d")!;
                    wordWidthSum = LayoutHarness.Descendants(block).SelectMany(box => box.Words).Sum(word => word.Width);
                    block.GetMinMaxWidth(graphics, out _, out maxWidth);
                    return Task.CompletedTask;
                });

            Assert.Equal(2 * ReplacedWidthPt, wordWidthSum, 0.01);
            Assert.Equal(wordWidthSum, maxWidth, 0.01);
        }

        // ─── Helpers ─────────────────────────────────────────────────────────────

        private static string Replaced(string kind) => kind switch
        {
            "image" => Image,
            "svg" => Svg,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
        };

        /// <summary>
        /// The horizontal distance between the end of the first word on the line and the start of the
        /// second — the gap the engine reserved between them.
        /// </summary>
        private static async Task<double> GapAfterFirstWordAsync(string inlineContent)
        {
            var words = await WordsAsync(inlineContent);

            Assert.Equal(2, words.Count);
            return words[1].Left - words[0].Right;
        }

        /// <summary>
        /// The atomic inline's own laid-out width, asserted alongside every gap so a "gap is zero"
        /// assertion cannot pass by the replaced content collapsing to nothing. 13px at the
        /// spec-correct 1px = 0.75pt.
        /// </summary>
        private const double ReplacedWidthPt = 13 * 0.75;

        private static async Task<List<CssRect>> WordsAsync(string inlineContent)
        {
            // A monospace font keeps the control's space width stable across whatever font the host
            // machine resolves for a generic family.
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                $"<div id='d' style='font:16px monospace'>{inlineContent}</div>"));

            var container = LayoutHarness.FindById(root, "d")!;

            return LayoutHarness.Descendants(container)
                .SelectMany(box => box.Words)
                .OrderBy(word => word.Left)
                .ToList();
        }
    }
}
