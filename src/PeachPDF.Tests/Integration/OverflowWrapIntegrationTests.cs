using PeachPDF.CSS;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Tests.TestSupport;
using System.Linq;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>End-to-end coverage for CSS Text 3's overflow-wrap property and word-wrap alias.</summary>
    public class OverflowWrapIntegrationTests
    {
        private const string LongWord = "Chargoggagoggmanchauggagoggchaubunagungamaugg";

        [Theory]
        [InlineData("overflow-wrap:break-word")]
        [InlineData("overflow-wrap:anywhere")]
        [InlineData("word-wrap:break-word")]
        [InlineData("word-wrap:anywhere")]
        public async Task EmergencyValues_WrapAnOverlongInlineWord(string declaration)
        {
            var html = LayoutHarness.Wrap($@"
                <p id='p' style='width:70pt'>before
                    <em id='word' style='{declaration}; letter-spacing:1pt'>{LongWord}</em>
                </p>");

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var paragraph = LayoutHarness.FindById(root, "p")!;
            var word = LayoutHarness.FindById(root, "word")!;
            var fragments = WordsOf(word).ToList();

            Assert.True(LinesWithText(paragraph) > 1);
            Assert.NotEmpty(fragments);
            Assert.All(fragments, fragment =>
                Assert.True(fragment.Right <= paragraph.ClientRight + 0.5,
                    $"'{fragment.Text}' overflowed {fragment.Right} > {paragraph.ClientRight}"));
        }

        [Fact]
        public async Task Normal_LeavesTheSameOverlongWordUnbroken()
        {
            var html = LayoutHarness.Wrap($"<p id='p' style='width:70pt'><em id='word'>{LongWord}</em></p>");
            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var paragraph = LayoutHarness.FindById(root, "p")!;
            var word = LayoutHarness.FindById(root, "word")!;
            var fragments = WordsOf(word).ToList();

            Assert.Equal(1, LinesWithText(paragraph));
            Assert.Contains(fragments, fragment => fragment.Right > paragraph.ClientRight + 0.5);
        }

        [Fact]
        public async Task NormalOpportunityBeforeWord_HasPriorityOverEmergencyBreakInsideIt()
        {
            var html = LayoutHarness.Wrap($@"
                <p id='p' style='width:70pt'>short
                    <em style='overflow-wrap:anywhere'>{LongWord}</em>
                </p>");

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var paragraph = LayoutHarness.FindById(root, "p")!;
            var textLines = paragraph.LineBoxes.Where(line => line.Words.Any(word => !string.IsNullOrEmpty(word.Text))).ToList();

            Assert.True(textLines.Count > 1);
            Assert.Equal("short", string.Concat(textLines[0].Words.Select(word => word.Text)));
        }

        [Fact]
        public async Task PropertyInheritsIntoInlineText()
        {
            var html = LayoutHarness.Wrap($@"
                <p id='p' style='width:70pt; overflow-wrap:anywhere'>
                    <em id='word'>{LongWord}</em>
                </p>");

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var paragraph = LayoutHarness.FindById(root, "p")!;
            var word = LayoutHarness.FindById(root, "word")!;

            Assert.Equal(OverflowWrap.Anywhere, word.OverflowWrap.Value);
            Assert.True(LinesWithText(paragraph) > 1);
        }

        [Fact]
        public async Task NoWrap_DisablesEmergencyWrapping()
        {
            var html = LayoutHarness.Wrap($@"
                <p id='p' style='width:70pt; white-space:nowrap; overflow-wrap:anywhere'>{LongWord}</p>");

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var paragraph = LayoutHarness.FindById(root, "p")!;

            Assert.Equal(1, LinesWithText(paragraph));
            Assert.Contains(WordsOf(paragraph), word => word.Right > paragraph.ClientRight + 0.5);
        }

        [Theory]
        [InlineData("nowrap")]
        [InlineData("pre")]
        public async Task NestedNonWrappingRun_DoesNotReduceItsParentsMinContentWidth(string whiteSpace)
        {
            var html = LayoutHarness.Wrap($@"
                <div style='display:grid; grid-template-columns:min-content'>
                    <div id='item'>
                        <span id='run' style='white-space:{whiteSpace}; overflow-wrap:anywhere'>{LongWord}</span>
                    </div>
                </div>");

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var item = LayoutHarness.FindById(root, "item")!;
            var run = LayoutHarness.FindById(root, "run")!;
            var word = Assert.Single(WordsOf(run));

            Assert.True(item.ClientRight + 0.5 >= word.Right,
                $"the {whiteSpace} run overflowed its min-content-sized parent: {word.Right} > {item.ClientRight}");
            Assert.True(item.ActualWidth + 0.5 >= word.Width,
                $"the parent was narrower than the unbreakable word: {item.ActualWidth} < {word.Width}");
        }

        [Fact]
        public async Task OrdinaryLayout_DoesNotEagerlyMeasureEveryAnywhereGraphemeForMinContent()
        {
            var text = string.Join(' ', Enumerable.Repeat("ninechars", 100));
            var html = LayoutHarness.Wrap($"<p id='p' style='width:400pt; overflow-wrap:anywhere'>{text}</p>");

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var words = WordsOf(LayoutHarness.FindById(root, "p")!).ToList();

            Assert.True(words.Count >= 100);
            Assert.All(words, word => Assert.Null(word.OverflowWrapMinWidth));
        }

        [Fact]
        public async Task CombiningSequence_RemainsOneGraphemeAtEveryEmergencyBreak()
        {
            const string grapheme = "a\u0301";
            var html = LayoutHarness.Wrap($@"
                <p id='p' style='width:18pt; overflow-wrap:anywhere'>{string.Concat(Enumerable.Repeat(grapheme, 12))}</p>");

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var paragraph = LayoutHarness.FindById(root, "p")!;

            Assert.True(LinesWithText(paragraph) > 1);
            Assert.All(paragraph.Words, word =>
                Assert.False(word.Text?.StartsWith('\u0301') == true, "a line began with a detached combining mark"));
        }

        [Fact]
        public async Task AnywhereButNotBreakWord_ReducesMinContentWidth()
        {
            static async Task<double> GridItemWidth(string value)
            {
                var html = LayoutHarness.Wrap($@"
                    <div style='display:grid; grid-template-columns:min-content'>
                        <span id='item' style='overflow-wrap:{value}'>{LongWord}</span>
                    </div>");
                var (root, _) = await LayoutHarness.LayoutAsync(html);
                return LayoutHarness.FindById(root, "item")!.ActualWidth;
            }

            var anywhere = await GridItemWidth("anywhere");
            var breakWord = await GridItemWidth("break-word");

            Assert.True(anywhere < breakWord / 2,
                $"anywhere should contribute grapheme opportunities to min-content: {anywhere} vs {breakWord}");
        }

        [Fact]
        public async Task AnywhereReducesAnAutoTablesMinContentWidth()
        {
            var html = LayoutHarness.Wrap($@"
                <div id='container' style='width:70pt'>
                    <table id='table' style='border-spacing:0'>
                        <tr><td id='cell' style='padding:0; overflow-wrap:anywhere'>{LongWord}</td></tr>
                    </table>
                </div>");

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var container = LayoutHarness.FindById(root, "container")!;
            var table = LayoutHarness.FindById(root, "table")!;
            var cell = LayoutHarness.FindById(root, "cell")!;

            Assert.True(table.ActualWidth <= container.ActualWidth + 0.5,
                $"the auto table exceeded its available width: {table.ActualWidth} > {container.ActualWidth}");
            Assert.All(WordsOf(cell), word => Assert.True(word.Right <= cell.ClientRight + 0.5,
                $"'{word.Text}' overflowed the intrinsically-sized cell"));
        }

        [Fact]
        public async Task VerticalWritingMode_UsesEmergencyBreaksAlongItsInlineAxis()
        {
            var html = LayoutHarness.Wrap($@"
                <p id='p' style='writing-mode:vertical-rl; width:80pt; height:70pt; overflow-wrap:anywhere'>
                    {LongWord}
                </p>");

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var paragraph = LayoutHarness.FindById(root, "p")!;

            Assert.True(LinesWithText(paragraph) > 1);
            Assert.All(WordsOf(paragraph), word =>
                Assert.True(word.Bottom <= paragraph.ClientBottom + 0.5,
                    $"'{word.Text}' overflowed {word.Bottom} > {paragraph.ClientBottom}"));
        }

        [Fact]
        public async Task FreshRelayout_RestoresAndRechoosesEmergencySplits()
        {
            var html = LayoutHarness.Wrap($@"
                <p id='p' style='width:70pt; overflow-wrap:anywhere'>{LongWord}</p>");

            var snapshots = await LayoutHarness.LayoutRepeatedlyAsync(html, 2, (root, _) =>
            {
                var paragraph = LayoutHarness.FindById(root, "p")!;
                return string.Join('|', paragraph.LineBoxes.Select(line =>
                    string.Concat(line.Words.Select(word => word.Text))));
            });

            Assert.Equal(snapshots[0], snapshots[1]);
            Assert.Contains('|', snapshots[0]);
        }

        private static int LinesWithText(CssBox box) =>
            box.LineBoxes.Count(line => line.Words.Any(word => !string.IsNullOrEmpty(word.Text)));

        private static System.Collections.Generic.IEnumerable<CssRect> WordsOf(CssBox box) =>
            LayoutHarness.Descendants(box).SelectMany(descendant => descendant.Words);
    }
}
