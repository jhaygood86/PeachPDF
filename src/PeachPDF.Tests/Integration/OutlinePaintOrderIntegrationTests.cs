using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core.Paint;
using PeachPDF.Tests.TestSupport;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Where an outline lands in the paint sequence. An outline is drawn once its enclosing outline
    /// scope (a stacking context, positioned box, float, atomic inline, flex/grid item, or a box with a
    /// transform or <c>clip-path</c>) has painted its content - ahead of its positive <c>z-index</c>
    /// layers, which still cover it - not the moment its own box finishes, which let the next sibling's
    /// background cover the ring. Asserted on the single ordered <see cref="TestRecordingGraphics.Log"/>,
    /// since the order across call types is the whole point.
    /// </summary>
    public class OutlinePaintOrderIntegrationTests
    {
        private static readonly RColor Ring = RColor.FromArgb(217, 74, 74);
        private static readonly RColor Gray = RColor.FromArgb(238, 238, 238);
        private static readonly RColor Blue = RColor.FromArgb(10, 20, 200);

        private const string Outline = "outline: 20pt solid rgb(217,74,74); outline-offset: 6pt";

        [Fact]
        public async Task FollowingSibling_PaintsUnderThePrecedingSiblingsOutline()
        {
            // The outline showcase's layout-neutral example: the ring overlaps "after", and must cover it.
            var g = await PaintAsync(
                "<div style='background: rgb(238,238,238)'>before</div>" +
                $"<div style='background: rgb(10,20,200); {Outline}; margin: 4pt 0'>outlined</div>" +
                "<div style='background: rgb(238,238,238)'>after</div>");

            var ring = IndexOfFill(g, Ring);
            var afterBackground = LastIndexOfFill(g, Gray);
            var afterText = g.Log.FindIndex(e => e is TestRecordingGraphics.DrawStringCall { Text: "after" });

            Assert.True(afterBackground >= 0 && afterText >= 0);
            Assert.True(ring > afterBackground, "the following sibling's background painted over the outline");
            Assert.True(ring > afterText, "the following sibling's text painted over the outline");
        }

        [Fact]
        public async Task Outline_StillPaintsOverItsOwnBoxsDescendants()
        {
            var g = await PaintAsync(
                $"<div style='{Outline}'><div style='background: rgb(10,20,200); margin: -10pt'>child</div></div>");

            Assert.True(IndexOfFill(g, Ring) > IndexOfFill(g, Blue));
        }

        [Fact]
        public async Task PositionedBox_DrawsTheOutlinesItHoldsBeforeALaterPositionedSiblingPaints()
        {
            // The relative box is its own scope: its child's outline goes over the child's own sibling
            // inside it, but is finished before the next positioned box - painted after it - starts.
            var g = await PaintAsync(
                "<div style='position: relative'>" +
                $"<div style='{Outline}'>outlined</div>" +
                "<div style='background: rgb(238,238,238)'>inside</div>" +
                "</div>" +
                "<div style='position: relative; background: rgb(10,20,200)'>next</div>");

            var ring = IndexOfFill(g, Ring);
            Assert.True(ring > IndexOfFill(g, Gray));
            Assert.True(ring < IndexOfFill(g, Blue));
        }

        [Fact]
        public async Task InlineBlock_DrawsItsOutlinesBeforeTheNextInlineBlockPaints()
        {
            // An atomic inline paints as a unit, outlines included, the way browsers paint it.
            var g = await PaintAsync(
                "<div>" +
                $"<span style='display: inline-block'><span style='{Outline}'>a</span></span>" +
                "<span style='display: inline-block; background: rgb(10,20,200)'>b</span>" +
                "</div>");

            Assert.True(IndexOfFill(g, Ring) < IndexOfFill(g, Blue));
        }

        [Fact]
        public async Task PositiveZIndexOverlay_PaintsOverAnOutlineBeneathIt()
        {
            var g = await PaintAsync(
                $"<div style='{Outline}; margin: 40pt'>outlined</div>" +
                "<div style='position: absolute; top: 0; left: 0; width: 300pt; height: 100pt; z-index: 5; background: rgb(10,20,200)'>overlay</div>");

            Assert.True(IndexOfFill(g, Ring) < IndexOfFill(g, Blue), "the outline was drawn over a z-index: 5 overlay");
        }

        [Fact]
        public async Task StackingContext_DrawsItsOwnOutlineUnderItsPositiveZIndexChildren()
        {
            var g = await PaintAsync(
                $"<div style='position: relative; z-index: 0; {Outline}; margin: 40pt'>" +
                "<div style='background: rgb(238,238,238)'>in flow</div>" +
                "<div style='position: absolute; top: -30pt; z-index: 2; background: rgb(10,20,200)'>raised</div>" +
                "</div>");

            var ring = IndexOfFill(g, Ring);
            Assert.True(ring > IndexOfFill(g, Gray), "the outline went under its own in-flow content");
            Assert.True(ring < IndexOfFill(g, Blue), "the outline was drawn over a positive z-index child");
            Assert.Single(g.Log, e => IsFillOf(e, Ring));
        }

        [Fact]
        public async Task ScopeWithRaisedChild_DrawsItsOutlineOverItsCollapsedBorders_AndUnderTheChild()
        {
            // Collapsed borders are painted after the table's in-flow content; an inset outline must
            // still land on top of them, and the raised cell content on top of both.
            var g = await PaintAsync(
                "<table style='border-collapse: collapse; position: relative; z-index: 0; margin: 30pt; " +
                "outline: 4pt solid rgb(217,74,74); outline-offset: -2pt'>" +
                "<tr><td style='border: 3pt solid rgb(10,200,20)'>a</td>" +
                "<td style='border: 3pt solid rgb(10,200,20)'>" +
                "<div style='position: relative; z-index: 1; background: rgb(10,20,200)'>raised</div></td></tr>" +
                "</table>");

            var ring = IndexOfFill(g, Ring);
            var lastBorder = LastIndexOfFill(g, RColor.FromArgb(10, 200, 20));

            Assert.True(lastBorder >= 0, "no collapsed border was painted");
            Assert.True(ring > lastBorder, "a collapsed border was painted over the outline");
            Assert.True(ring < IndexOfFill(g, Blue), "the outline was drawn over a positive z-index child");
        }

        [Fact]
        public async Task ZIndexAutoPositionedSibling_StillPaintsUnderTheOutline()
        {
            // Unlike a positive z-index layer, a following z-index: auto positioned box is not raised
            // over the ring - it would otherwise cover it exactly as a following in-flow sibling used to.
            var g = await PaintAsync(
                $"<div style='{Outline}'>outlined</div>" +
                "<div style='position: relative; background: rgb(238,238,238)'>after</div>");

            Assert.True(IndexOfFill(g, Ring) > IndexOfFill(g, Gray));
        }

        [Fact]
        public async Task DeferredOutline_IsDrawnUnderTheOverflowClipOfTheBoxBetweenItAndItsScope()
        {
            var html =
                "<div id='clip' style='overflow: hidden; width: 200pt; height: 60pt; margin: 30pt'>" +
                $"<div id='o' style='{Outline}'>outlined</div>" +
                "</div>" +
                "<div style='background: rgb(238,238,238)'>after</div>";
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(html));
            var outlined = LayoutHarness.FindById(root, "o")!;
            var expectedClip = FragmentPaintHarness.FragmentOf(container, outlined).OverflowClip;
            Assert.NotNull(expectedClip);

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintPage(container, g);

            var ring = IndexOfFill(g, Ring);
            Assert.True(ring > LastIndexOfFill(g, Gray), "the outline was not deferred past the clipping box");
            Assert.Contains(expectedClip!.Value, ActiveClipsAt(g, ring));
        }

        [Fact]
        public async Task TransformedBox_DrawsTheOutlinesItHoldsInsideItsOwnTransform()
        {
            var g = await PaintAsync(
                "<div style='transform: rotate(5deg)'>" +
                $"<div style='{Outline}'>outlined</div>" +
                "<div style='background: rgb(238,238,238)'>inside</div>" +
                "</div>");

            var ring = IndexOfFill(g, Ring);
            var push = g.Log.FindIndex(e => e is TestRecordingGraphics.PushTransformCall);
            var pop = g.Log.FindIndex(e => e is TestRecordingGraphics.PopTransformCall);

            Assert.True(ring > IndexOfFill(g, Gray));
            Assert.True(push < ring && ring < pop, "the outline escaped its box's transform");
        }

        [Fact]
        public async Task PaintingOutsideAnyScope_DrawsTheOutlineInPlace()
        {
            // PaintContent is reachable without PaintTagged having opened a scope; nothing would ever
            // draw an outline deferred there, so it is drawn at once.
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                $"<div id='o' style='{Outline}'>outlined</div>"));
            var fragment = FragmentPaintHarness.FragmentOf(container, LayoutHarness.FindById(root, "o")!);

            var g = new TestRecordingGraphics();
            new FragmentPainter(container).PaintContent(g, fragment);

            Assert.True(IndexOfFill(g, Ring) >= 0);
        }

        [Theory]
        [InlineData("", "", false)]
        [InlineData("", "display: inline", false)]
        [InlineData("", "position: relative", true)]
        [InlineData("", "position: absolute", true)]
        [InlineData("", "float: left", true)]
        [InlineData("", "display: inline-block", true)]
        [InlineData("", "opacity: 0.5", true)]
        [InlineData("", "transform: rotate(5deg)", true)]
        [InlineData("", "clip-path: inset(2pt)", true)]
        [InlineData("display: flex", "", true)]
        [InlineData("display: grid", "", true)]
        public async Task EstablishesOutlineScope_ForBoxesThatPaintAsOneUnit(string parentStyle, string style, bool expected)
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                $"<div style='{parentStyle}'><div id='b' style='{style}'>x</div></div>"));

            Assert.Equal(expected, FragmentPainter.EstablishesOutlineScope(LayoutHarness.FindById(root, "b")!));
        }

        private static async Task<TestRecordingGraphics> PaintAsync(string body)
        {
            var (_, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(body));
            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintPage(container, g);
            return g;
        }

        private static bool IsFillOf(object entry, RColor color) => entry switch
        {
            TestRecordingGraphics.DrawRectCall r => r.Color == color,
            TestRecordingGraphics.DrawPathCall { Stroked: false } p => p.Color == color,
            TestRecordingGraphics.DrawPolygonCall p => p.Color == color,
            _ => false
        };

        private static int IndexOfFill(TestRecordingGraphics g, RColor color) =>
            g.Log.FindIndex(e => IsFillOf(e, color));

        private static int LastIndexOfFill(TestRecordingGraphics g, RColor color) =>
            g.Log.FindLastIndex(e => IsFillOf(e, color));

        /// <summary>The rectangular clips still pushed when the log reaches <paramref name="index"/>.</summary>
        private static List<RRect> ActiveClipsAt(TestRecordingGraphics g, int index)
        {
            var stack = new List<RRect>();
            foreach (var entry in g.Log.Take(index))
            {
                if (entry is TestRecordingGraphics.PushClipCall push) stack.Add(push.Rect);
                else if (entry is TestRecordingGraphics.PopClipCall && stack.Count > 0) stack.RemoveAt(stack.Count - 1);
            }

            return stack;
        }
    }
}
