using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core.Utils;
using PeachPDF.Tests.TestSupport;
using System.Linq;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// <c>border-style: hidden</c> is "same as <c>none</c>" for everything except a border-collapsed
    /// table's conflict resolution
    /// (<see href="https://www.w3.org/TR/css-backgrounds-3/#border-style">css-backgrounds-3 §3.2</see>),
    /// so the computed <c>border-*-width</c> is 0
    /// (<see href="https://www.w3.org/TR/css-backgrounds-3/#the-border-width">§3.3</see>) and the edge
    /// occupies no space at all. The same rule holds for <c>column-rule-width</c> against
    /// <c>column-rule-style: hidden</c>
    /// (<see href="https://www.w3.org/TR/css-multicol-1/#crw">css-multicol-1 §4.4</see>).
    /// </summary>
    /// <remarks>
    /// Paint already skipped a hidden edge, but the used width was zeroed only for <c>none</c> - so a
    /// hidden border drew nothing while still reserving its declared width, inflating the border box by
    /// the full border on every side (visible in the border-style showcase as a <c>hidden</c> swatch
    /// taller and wider than its <c>none</c> neighbour, since the background paints over the border box).
    /// </remarks>
    public class HiddenBorderWidthTests
    {
        /// <summary>
        /// Lays out a 200pt × 48pt content box inside a wider wrapper and returns its border box. The
        /// box states its own <c>width</c> deliberately: an <c>auto</c>-width block absorbs its inline
        /// borders into the width it takes from its containing block, so its border box measures the
        /// containing block either way and no assertion on it could ever bite on the left/right sides.
        /// With a stated width the border box is content + left + right, and each axis is pinned.
        /// </summary>
        private static async Task<(double Width, double Height)> BorderBoxAsync(string declaration)
        {
            var (root, _) = await LayoutHarness.LayoutAsync(
                "<!DOCTYPE html><html><body><div style='width: 400pt'>"
                + $"<div id='box' style='margin: 0; width: 200pt; height: 48pt; {declaration}'></div>"
                + "</div></body></html>");

            var box = LayoutHarness.FindById(root, "box")!;

            return (box.ActualRight - box.Location.X, box.ActualBottom - box.Location.Y);
        }

        [Theory]
        // A hidden border reserves nothing on any side: the border box is exactly the 200pt × 48pt
        // content box, identical to `none` and unlike any drawn style.
        [InlineData("border: 16pt hidden #4a90d9")]
        [InlineData("border: 16pt none #4a90d9")]
        [InlineData("border-style: hidden; border-width: 16pt")]
        public async Task AHiddenBorder_OccupiesNoSpace(string declaration)
        {
            Assert.Equal((200d, 48d), await BorderBoxAsync(declaration));
        }

        [Fact]
        public async Task ADrawnBorder_StillOccupiesItsWidth()
        {
            Assert.Equal((232d, 80d), await BorderBoxAsync("border: 16pt solid #4a90d9"));
        }

        [Theory]
        // Per side, too: only the hidden edges collapse away. The left/right case is the one that pins
        // the inline axis - before the fix it measured 232pt wide, like the all-solid box.
        [InlineData("border: 16pt solid; border-top-style: hidden", 232, 64)]
        [InlineData("border: 16pt solid; border-left-style: hidden; border-right-style: hidden", 200, 80)]
        [InlineData("border: 16pt solid; border-left-style: hidden", 216, 80)]
        [InlineData("border: 16pt hidden; border-bottom-style: solid", 200, 64)]
        public async Task HiddenAppliesPerSide(string declaration, double width, double height)
        {
            Assert.Equal((width, height), await BorderBoxAsync(declaration));
        }

        [Theory]
        // The declared-width accessors a collapsed table resolves against follow the same rule. They
        // deliberately bypass the Actual* cache (which a collapsed pass overwrites with the used
        // half-width), so they zero `hidden` on their own rather than inheriting it.
        [InlineData("hidden", 0d, 0d, 0d, 0d)]
        [InlineData("none", 0d, 0d, 0d, 0d)]
        [InlineData("solid", 16d, 16d, 16d, 16d)]
        public async Task TheNaturalBorderWidths_ZeroAHiddenSideToo(
            string style, double top, double right, double bottom, double left)
        {
            var (root, _) = await LayoutHarness.LayoutAsync(
                "<!DOCTYPE html><html><body>"
                + $"<div id='box' style='height: 48pt; border: 16pt {style} #4a90d9'></div>"
                + "</body></html>");

            var box = LayoutHarness.FindById(root, "box")!;

            Assert.Equal(
                (top, right, bottom, left),
                (box.NaturalBorderTopWidth, box.NaturalBorderRightWidth,
                    box.NaturalBorderBottomWidth, box.NaturalBorderLeftWidth));
        }

        /// <summary>
        /// <c>outline-style</c> has the same cache shape as the border widths - <c>ActualOutlineWidth</c>
        /// caches on first read and zeroes for <c>OutlineStyle.None</c> - so it needs the same hook.
        /// An outline reserves no layout space, so this is a latent-consistency fix rather than a
        /// visual one, exactly like the border case before a reader moves earlier.
        /// </summary>
        [Theory]
        [InlineData("solid", "none", 0d)]
        [InlineData("none", "solid", 16d)]
        public async Task WritingTheOutlineStyle_InvalidatesAnAlreadyReadOutlineWidth(
            string before, string after, double expected)
        {
            var (root, _) = await LayoutHarness.LayoutAsync(
                "<!DOCTYPE html><html><body>"
                + $"<div id='box' style='width: 100pt; height: 48pt; outline: 16pt {before} red'>x</div>"
                + "</body></html>");

            var box = LayoutHarness.FindById(root, "box")!;
            var parser = new PeachPDF.Html.Core.Parse.CssValueParser(new PeachPDF.Adapters.PdfSharpAdapter());

            _ = box.ActualOutlineWidth;
            CssUtils.SetPropertyValue(parser, box, "outline-style", after);

            Assert.Equal(expected, box.ActualOutlineWidth, 3);
        }

        /// <summary>
        /// A <c>border-*-style</c> write invalidates the cached used width that depends on it. The
        /// <c>Actual*</c> family caches on first read, and until this change only <c>border-*-width</c>
        /// carried an <c>invalidates</c> hook - so reading the width, then writing the style, left the
        /// stale value in place. The hazard predates <c>hidden</c> (<c>none</c> had it too), but
        /// zeroing the width for <c>hidden</c> gives it a second style that changes the answer.
        /// </summary>
        [Theory]
        [InlineData("solid", "hidden", 0d)]
        [InlineData("solid", "none", 0d)]
        [InlineData("hidden", "solid", 16d)]
        [InlineData("none", "solid", 16d)]
        public async Task WritingTheStyle_InvalidatesAnAlreadyReadWidth(
            string before, string after, double expected)
        {
            var (root, _) = await LayoutHarness.LayoutAsync(
                "<!DOCTYPE html><html><body>"
                + $"<div id='box' style='width: 100pt; height: 48pt; border: 16pt {before} #4a90d9'></div>"
                + "</body></html>");

            var box = LayoutHarness.FindById(root, "box")!;
            var parser = new PeachPDF.Html.Core.Parse.CssValueParser(new PeachPDF.Adapters.PdfSharpAdapter());

            // Force every side's width into the cache before the style changes underneath it.
            _ = (box.ActualBorderTopWidth, box.ActualBorderRightWidth,
                 box.ActualBorderBottomWidth, box.ActualBorderLeftWidth);

            foreach (var side in new[] { "top", "right", "bottom", "left" })
            {
                CssUtils.SetPropertyValue(parser, box, $"border-{side}-style", after);
            }

            Assert.Equal(
                (expected, expected, expected, expected),
                (box.ActualBorderTopWidth, box.ActualBorderRightWidth,
                    box.ActualBorderBottomWidth, box.ActualBorderLeftWidth));
        }

        [Theory]
        [InlineData("hidden", 0d)]
        [InlineData("none", 0d)]
        [InlineData("solid", 16d)]
        public async Task AHiddenColumnRule_HasZeroUsedWidth(string style, double expected)
        {
            var (root, _) = await LayoutHarness.LayoutAsync(
                "<!DOCTYPE html><html><body>"
                + $"<div id='cols' style='columns: 2; column-gap: 40pt; column-rule: 16pt {style} #333'>"
                + "text</div></body></html>");

            var cols = LayoutHarness.FindById(root, "cols")!;

            Assert.Equal(expected, cols.ActualColumnRuleWidth, 3);
        }

        /// <summary>
        /// The width above is only half the story, and the half that was never visible. A zero used
        /// width is what stops the rule being drawn - so assert on paint, not on the number that feeds
        /// it: before the fix, <c>column-rule: 16pt hidden</c> drew a <em>solid</em> 16pt line, because
        /// <c>PaintColumnRules</c>' dash-style switch folds every style it does not name into
        /// <c>Solid</c> and nothing upstream had rejected <c>hidden</c>.
        /// </summary>
        [Theory]
        [InlineData("hidden", 0)]
        [InlineData("none", 0)]
        // The control: an identical container whose rule really is drawn, so a failure to record any
        // DrawLine at all reads as a broken harness rather than as the feature working. One rule for
        // one internal gap - asserting the exact count, not merely "some line was drawn", so a rule
        // painted twice or per-segment-doubled would fail here too.
        [InlineData("solid", 1)]
        public async Task AHiddenColumnRule_IsNotPainted(string style, int expectedRules)
        {
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                $"<div id='cols' style='width: 400pt; columns: 2; column-gap: 40pt; "
                + $"column-rule: 16pt {style} rgb(10,20,30)'>"
                + "<div style='height: 40pt'></div><div style='height: 40pt'></div></div>"));

            var cols = LayoutHarness.FindById(root, "cols")!;
            Assert.NotEmpty(cols.ColumnRuleSegments!);

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, cols, g);

            var rules = g.Log.OfType<TestRecordingGraphics.DrawLineCall>()
                .Where(l => l.Color == RColor.FromArgb(10, 20, 30));

            Assert.Equal(expectedRules, rules.Count());
        }

        /// <summary>
        /// Reaches <c>PaintColumnRules</c>' own <c>none</c>/<c>hidden</c> guard, which ordinary paint
        /// cannot: once the used width is 0 the call site's <c>ActualColumnRuleWidth &gt; 0</c> check
        /// keeps a suppressed rule out of the method entirely.
        /// </summary>
        /// <remarks>
        /// The route in is the exact scenario the guard exists for, not a contrivance:
        /// <c>_actualColumnRuleWidth</c> has **no invalidator** (neither `column-rule-width` nor
        /// `column-rule-style` carries an `invalidates` hook, and there is no
        /// `InvalidateColumnRuleWidth` to hook up), so priming the cache from a drawn rule and then
        /// writing the style leaves a stale non-zero width behind. The call site waves that through,
        /// and only the guard stops a solid 16pt line being painted - which is what
        /// <c>column-rule: 16pt hidden</c> did before this change.
        /// </remarks>
        [Theory]
        [InlineData("hidden")]
        [InlineData("none")]
        public async Task AStaleColumnRuleWidth_IsStoppedByThePaintGuard(string style)
        {
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='cols' style='width: 400pt; columns: 2; column-gap: 40pt; "
                + "column-rule: 16pt solid rgb(10,20,30)'>"
                + "<div style='height: 40pt'></div><div style='height: 40pt'></div></div>"));

            var cols = LayoutHarness.FindById(root, "cols")!;
            var parser = new PeachPDF.Html.Core.Parse.CssValueParser(new PeachPDF.Adapters.PdfSharpAdapter());

            Assert.NotEmpty(cols.ColumnRuleSegments!);
            Assert.Equal(16d, cols.ActualColumnRuleWidth, 3);   // prime the cache from a drawn rule

            CssUtils.SetPropertyValue(parser, cols, "column-rule-style", style);

            // The stale width survives the style write - that is the hazard, asserted rather than
            // assumed, so this test starts failing the day an invalidator is added rather than
            // silently ceasing to exercise the guard.
            Assert.Equal(16d, cols.ActualColumnRuleWidth, 3);

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, cols, g);

            Assert.DoesNotContain(g.Log.OfType<TestRecordingGraphics.DrawLineCall>(),
                l => l.Color == RColor.FromArgb(10, 20, 30));
        }
    }
}
