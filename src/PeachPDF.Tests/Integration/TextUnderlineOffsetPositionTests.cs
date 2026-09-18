using PeachPDF.Tests.TestSupport;
using System.Linq;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Issue #1118: <c>text-underline-offset</c>
    /// (<see href="https://www.w3.org/TR/css-text-decor-4/#underline-offset">css-text-decor-4 §2.8</see>)
    /// and <c>text-underline-position</c>
    /// (<see href="https://www.w3.org/TR/css-text-decor-4/#text-underline-position-property">css-text-decor-4
    /// §2.5</see> - <c>from-font</c> does not exist in css-text-decor-3 at all, so the full compound
    /// grammar this covers, <c>auto | [ from-font | under ] || [ left | right ]</c>, is a level-4
    /// addition) were previously unimplemented and silently dropped at parse time; this class covers the
    /// <c>auto</c>/<c>from-font</c>/<c>under</c> half - see <c>TextUnderlinePositionSideTests</c> for the
    /// <c>left</c>/<c>right</c> half (issue #1146). Neither affects <c>overline</c>/<c>line-through</c>,
    /// only <c>underline</c>.
    /// </summary>
    public class TextUnderlineOffsetPositionTests
    {
        [Fact]
        public async Task Offset_Auto_MatchesTheOrdinaryUnderlinePosition()
        {
            var withoutOffset = await UnderlineYAsync("");
            var withAutoOffset = await UnderlineYAsync("text-underline-offset:auto");

            Assert.Equal(withoutOffset, withAutoOffset, 3);
        }

        [Fact]
        public async Task Offset_PositiveLength_MovesTheUnderlineFurtherFromTheText()
        {
            var baseline = await UnderlineYAsync("");
            var offset = await UnderlineYAsync("text-underline-offset:5pt");

            // "Further from the text" for an ordinary (auto-position) underline means further down,
            // i.e. a larger Y - see PaintDecoration's ResolveUnderlineCross.
            Assert.Equal(baseline + 5, offset, 3);
        }

        [Fact]
        public async Task Offset_NegativeLength_MovesTheUnderlineCloserToTheText()
        {
            var baseline = await UnderlineYAsync("");
            var offset = await UnderlineYAsync("text-underline-offset:-3pt");

            Assert.Equal(baseline - 3, offset, 3);
        }

        [Fact]
        public async Task Offset_Percentage_ResolvesAgainstTheElementsOwnFontSize()
        {
            var withoutOffset = await UnderlineYAsync("font-size:20pt");
            var withOffset = await UnderlineYAsync("font-size:20pt; text-underline-offset:10%");

            Assert.Equal(withoutOffset + 2.0 /* 10% of 20pt */, withOffset, 3);
        }

        [Fact]
        public async Task Offset_DoesNotAffectOverlineOrLineThrough()
        {
            var overlineWithout = await DecorationYAsync("overline", "");
            var overlineWithOffset = await DecorationYAsync("overline", "text-underline-offset:20pt");
            var throughWithout = await DecorationYAsync("line-through", "");
            var throughWithOffset = await DecorationYAsync("line-through", "text-underline-offset:20pt");

            Assert.Equal(overlineWithout, overlineWithOffset, 3);
            Assert.Equal(throughWithout, throughWithOffset, 3);
        }

        [Fact]
        public async Task Position_Under_SitsAtTheRectanglesOwnBottomEdge()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<span id='s' style='text-decoration:underline; text-underline-position:under'>text</span>"),
                margin: 0);
            var s = LayoutHarness.FindById(root, "s")!;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, s, g);

            var line = Assert.Single(g.Log.OfType<TestRecordingGraphics.DrawLineCall>());
            var rect = s.Rectangles.Values.Single();

            Assert.Equal(rect.Bottom, line.Y1, 3);
        }

        [Fact]
        public async Task Position_FromFont_UsesTheFontsOwnUnderlinePositionMetric()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<span id='s' style='text-decoration:underline; text-underline-position:from-font'>text</span>"),
                margin: 0);
            var s = LayoutHarness.FindById(root, "s")!;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, s, g);

            var line = Assert.Single(g.Log.OfType<TestRecordingGraphics.DrawLineCall>());
            var rect = s.Rectangles.Values.Single();
            var expectedY = rect.Top + s.ActualFont.TextBaselineOffset - s.ActualFont.UnderlinePosition;

            Assert.Equal(expectedY, line.Y1, 3);
        }

        [Fact]
        public async Task Position_Auto_MustBeAtOrBelowTheAlphabeticBaseline()
        {
            // css-text-decor-3 §2.5: 'auto' "must be placed at or under the alphabetic baseline".
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<span id='s' style='text-decoration:underline'>text</span>"),
                margin: 0);
            var s = LayoutHarness.FindById(root, "s")!;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, s, g);

            var line = Assert.Single(g.Log.OfType<TestRecordingGraphics.DrawLineCall>());
            var rect = s.Rectangles.Values.Single();
            var baseline = rect.Top + s.ActualFont.TextBaselineOffset;

            Assert.True(line.Y1 >= baseline, "auto must not place the underline above the baseline");
        }

        [Fact]
        public void RFont_DefaultUnderlinePosition_IsZero()
        {
            // The base RFont default - every RFont except the OpenType-descriptor-backed FontAdapter
            // gets this (the same "plain default, real metric only from the real adapter" pattern
            // UnderlineThickness already uses).
            Assert.Equal(0, new TestFont(20).UnderlinePosition);
        }

        // ─── Helpers ─────────────────────────────────────────────────────────────

        private static Task<double> UnderlineYAsync(string extraStyle) => DecorationYAsync("underline", extraStyle);

        private static async Task<double> DecorationYAsync(string decorationLine, string extraStyle)
        {
            var style = string.IsNullOrEmpty(extraStyle)
                ? $"text-decoration:{decorationLine}"
                : $"text-decoration:{decorationLine}; {extraStyle}";
            var (root, container) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap($"<span id='s' style='{style}'>text</span>"), margin: 0);
            var s = LayoutHarness.FindById(root, "s")!;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, s, g);

            return Assert.Single(g.Log.OfType<TestRecordingGraphics.DrawLineCall>()).Y1;
        }
    }
}
