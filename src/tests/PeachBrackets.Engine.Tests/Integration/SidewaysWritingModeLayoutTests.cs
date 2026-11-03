using PeachBrackets.Html.Dom;
using PeachPDF.Rendering;
using PeachPDF.Tests.TestSupport;

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace PeachBrackets.Engine.Tests.Integration
{
    /// <summary>
    /// <c>writing-mode: sideways-rl</c>/<c>sideways-lr</c> (CSS Writing Modes 4 section 4.1) are vertical writing modes: lines stack
    /// along the horizontal axis exactly as for <c>vertical-rl</c>/<c>vertical-lr</c>, and their glyphs are always sideways
    /// (section 5.1). <c>sideways-lr</c> also reads bottom to top.
    /// </summary>
    public class SidewaysWritingModeLayoutTests
    {
        private const string Text = "alpha beta gamma delta epsilon zeta eta theta";

        private static async Task<(CssBox Box, List<CssRectWord> Words)> LayoutAsync(string style, string text = Text)
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                $"<div id='d' style='margin:0;padding:0;width:120pt;height:150pt;font-size:12pt;font-family:monospace;{style}'>{text}</div>"));
            var box = LayoutHarness.FindById(root, "d")!;
            var words = LayoutHarness.Descendants(box).SelectMany(b => b.Words.OfType<CssRectWord>()).Where(w => w.Text != "\n").ToList();
            return (box, words);
        }

        [Fact]
        public async Task SidewaysRl_LaysOutLikeVerticalRl_WithSidewaysGlyphs()
        {
            var (_, vertical) = await LayoutAsync("writing-mode:vertical-rl;text-orientation:sideways");
            var (box, sideways) = await LayoutAsync("writing-mode:sideways-rl");

            Assert.Equal(vertical.Count, sideways.Count);
            Assert.True(sideways.Count > 3);
            for (var i = 0; i < vertical.Count; i++)
            {
                Assert.Equal(vertical[i].Left, sideways[i].Left, 0.01);
                Assert.Equal(vertical[i].Top, sideways[i].Top, 0.01);
                Assert.Equal(vertical[i].Width, sideways[i].Width, 0.01);
                Assert.Equal(vertical[i].Height, sideways[i].Height, 0.01);
            }

            // The words run down a column (Top grows) and the columns stack leftwards from the right edge.
            Assert.True(sideways.Select(w => w.Left).Distinct().Count() > 1, "the text wrapped into more than one column");
            Assert.True(sideways.Max(w => w.Right) <= box.ClientRight + 0.01);
            Assert.Equal(sideways.Max(w => w.Right), sideways[0].Right, 0.01);
            Assert.True(sideways[1].Top > sideways[0].Top);
        }

        [Fact]
        public async Task SidewaysLr_StacksColumnsLikeVerticalLr()
        {
            var (_, vertical) = await LayoutAsync("writing-mode:vertical-lr;text-orientation:sideways");
            var (box, sideways) = await LayoutAsync("writing-mode:sideways-lr");

            Assert.Equal(vertical.Count, sideways.Count);

            // The first column hangs from the left edge and the next one starts to its right.
            var columns = sideways.Select(w => w.Left).Distinct().OrderBy(x => x).ToList();
            Assert.True(columns.Count > 1);
            Assert.Equal(box.ClientLeft, columns[0], 0.5);
            Assert.Equal(vertical.Select(w => w.Left).Distinct().OrderBy(x => x).ToList().Count, columns.Count);
        }

        [Fact]
        public async Task SidewaysLr_ReadsBottomToTop_FromTheBottomEdge()
        {
            var (box, words) = await LayoutAsync("writing-mode:sideways-lr", "alpha beta gamma");

            var column = words.Where(w => w.Left == words[0].Left).ToList();
            Assert.True(column.Count >= 2);

            // The first word starts at the bottom of the box and each later word sits above the one before it.
            Assert.Equal(box.ClientBottom, column[0].Bottom, 0.5);
            for (var i = 1; i < column.Count; i++)
                Assert.True(column[i].Bottom <= column[i - 1].Top + 0.5, $"word {i} is above word {i - 1}");
        }

        [Fact]
        public async Task SidewaysLr_TextAlignEnd_MovesTheLineToTheTop()
        {
            var (box, words) = await LayoutAsync("writing-mode:sideways-lr;text-align:end", "alpha");

            Assert.Equal(box.ClientTop, words[0].Top, 0.5);
        }

        [Fact]
        public async Task SidewaysRl_TextAlignEnd_MovesTheLineToTheBottom()
        {
            var (box, words) = await LayoutAsync("writing-mode:sideways-rl;text-align:end", "alpha");

            Assert.Equal(box.ClientBottom, words[0].Bottom, 0.5);
        }

        [Theory]
        [InlineData("sideways-rl")]
        [InlineData("sideways-lr")]
        public async Task TextOrientation_DoesNotChangeSidewaysGlyphs(string writingMode)
        {
            var (_, plain) = await LayoutAsync($"writing-mode:{writingMode}", "日本語");
            var (box, upright) = await LayoutAsync($"writing-mode:{writingMode};text-orientation:upright", "日本語");

            Assert.Equal(plain.Count, upright.Count);
            for (var i = 0; i < plain.Count; i++)
            {
                Assert.Equal(plain[i].Width, upright[i].Width, 0.01);
                Assert.Equal(plain[i].Height, upright[i].Height, 0.01);
            }

            Assert.Equal("Sideways", box.EffectiveTextOrientation.ToString());
        }

        [Theory]
        [InlineData("sideways-rl", 1, -1)]
        [InlineData("sideways-lr", -1, 1)]
        public async Task TheGlyphsAreTurnedClockwiseForRlAndCounterClockwiseForLr(string writingMode, float m12, float m21)
        {
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                $"<div id='d' style='margin:0;width:120pt;height:150pt;font-size:12pt;writing-mode:{writingMode}'>alpha</div>"));
            var box = LayoutHarness.FindById(root, "d")!;
            var word = LayoutHarness.Descendants(box).SelectMany(b => b.Words.OfType<CssRectWord>()).First(w => w.Text == "alpha");

            var recording = new RecordingGraphics(new HtmlRenderContext());
            FragmentPaintHarness.PaintBox(container, box, recording);

            // The transform pushed just before the word is drawn turns the natural run about its own footprint.
            var log = recording.Log.ToList();
            var draw = log.FindIndex(op => op.Kind == PaintOpKind.DrawString && op.Text == "alpha");
            Assert.True(draw > 0);
            var push = log.Take(draw).Last(op => op.Kind == PaintOpKind.PushTransform).Matrix!.Value;

            Assert.Equal(0, push.M11);
            Assert.Equal(m12, push.M12);
            Assert.Equal(m21, push.M21);
            Assert.Equal(0, push.M22);

            // Natural (0, 0) - the start of the run - lands on the word's footprint: the top edge for clockwise
            // turning, the bottom edge for counter-clockwise (where the run reads upward from there).
            var start = System.Numerics.Vector2.Transform(System.Numerics.Vector2.Zero, push);
            Assert.Equal(m12 > 0 ? word.Top : word.Bottom, start.Y, 0.5);
        }

        [Theory]
        [InlineData("vertical-rl", "upright", "Upright")]
        [InlineData("vertical-lr", "mixed", "Mixed")]
        [InlineData("horizontal-tb", "upright", "Upright")]
        [InlineData("sideways-rl", "mixed", "Sideways")]
        [InlineData("sideways-lr", "upright", "Sideways")]
        public async Task EffectiveTextOrientation_IsSidewaysOnlyUnderTheSidewaysModes(string writingMode, string orientation, string expected)
        {
            var (box, _) = await LayoutAsync($"writing-mode:{writingMode};text-orientation:{orientation}", "x");

            Assert.Equal(expected, box.EffectiveTextOrientation.ToString());
        }
    }
}
