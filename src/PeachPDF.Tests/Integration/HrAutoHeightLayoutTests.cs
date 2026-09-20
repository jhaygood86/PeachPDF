using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Tests.TestSupport;
using System.Linq;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// An <c>&lt;hr&gt;</c>'s used height, and the two things measured against it: where the next
    /// in-flow sibling starts, and how tall a container holding only the rule is.
    /// <para>
    /// All three used to be a constant 2 units. <c>CssBoxHr.PerformLayoutImp</c> read
    /// <c>Size.Height + ActualBorderTopWidth + ActualBorderBottomWidth</c>, but
    /// <c>PlaceAsBlockChild</c> has already written <c>ActualBottom = Location.Y</c> by then, and that
    /// setter stores <c>Size.Height = value - ActualBoxSizeIncludedHeight - Location.Y</c> — so
    /// <c>Size.Height</c> is exactly <c>-(borderTop + borderBottom)</c> and the sum cancels to zero
    /// every time. The branch was unreachable and every rule fell through to a hard-coded
    /// <c>height = 2</c> (issues #1229, #1232).
    /// </para>
    /// </summary>
    public class HrAutoHeightLayoutTests
    {
        // 1px = 0.75pt, so a 1px border is 0.75 and the UA default rule is 1.5 tall.
        [Theory]
        [InlineData("", 1.5)]                                // UA default: 1px top + 1px bottom
        [InlineData("border: 0.5pt solid", 1.0)]
        [InlineData("border: 2px solid", 3.0)]
        [InlineData("border: 6px groove", 9.0)]
        [InlineData("border: 10px solid", 15.0)]
        [InlineData("height: 0", 1.5)]                       // content 0, plus the default borders
        [InlineData("height: 10px", 9.0)]                    // 7.5 content + 1.5 default borders
        [InlineData("border: 2px solid; height: 10px", 10.5)]
        [InlineData("border: none; height: 10px", 7.5)]
        public async Task Hr_UsedHeight_IsItsContentPlusItsOwnBorders(string declaration, double expected)
        {
            var (root, container) = await LayoutAsync(declaration);
            var hr = LayoutHarness.FindById(root, "hr")!;

            Assert.Equal(expected, FragmentPaintHarness.FragmentOf(container, hr).WholeBoxRect.Height, 3);
            Assert.Equal(expected, hr.ActualBottom - hr.Location.Y, 3);
        }

        [Theory]
        [InlineData("")]
        [InlineData("border: 0.5pt solid")]
        [InlineData("border: 2px solid")]
        [InlineData("border: 6px groove")]
        public async Task AutoHeightHr_HasNoContentBetweenItsBorders(string declaration)
        {
            // CSS 2.1 §10.6.3: an auto-height block-level box with no in-flow children has a used
            // content height of 0. The leftover `2 - borders` used to be real content, and painted as a
            // visible gap between the two border edges - 0.5pt on the default rule, and a full 1pt on a
            // 0.5pt-bordered one, wider than either border it separated.
            var (root, container) = await LayoutAsync(declaration);
            var hr = LayoutHarness.FindById(root, "hr")!;

            var borders = hr.ActualBorderTopWidth + hr.ActualBorderBottomWidth;
            var borderBox = FragmentPaintHarness.FragmentOf(container, hr).WholeBoxRect.Height;

            Assert.Equal(0, borderBox - borders, 3);
        }

        [Fact]
        public async Task DefaultHr_PaintsItsTwoBordersMeetingOnOneEdge()
        {
            // The same fact at the other end of the pipeline, which is where it was actually noticed: a
            // default rule drew three bands at high zoom - #9a9a9a, white, #eeeeee - where a browser
            // draws two.
            //
            // Asserted on the distinct Y coordinates of everything painted, not on a band count or a
            // bounding box. The count was always two draw calls, and a bevel's two calls are mitred
            // L-shaped pairs (top+left, bottom+right), so each one's bounding box spans the whole rule
            // either way. What the gap actually added was a fourth Y: the two borders stopped sharing
            // an inner edge.
            var (root, container) = await LayoutAsync("");
            var hr = LayoutHarness.FindById(root, "hr")!;
            var rect = FragmentPaintHarness.FragmentOf(container, hr).WholeBoxRect;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, hr, g);

            var ys = g.Log
                .SelectMany(entry => entry switch
                {
                    TestRecordingGraphics.DrawPathCall path => path.Points.Select(point => point.Y),
                    TestRecordingGraphics.DrawPolygonCall polygon => polygon.Points.Select(point => point.Y),
                    _ => [],
                })
                .Select(y => System.Math.Round(y, 3))
                .Distinct()
                .OrderBy(y => y)
                .ToList();

            // Outer top, the one shared inner edge, outer bottom. A gap would make it four.
            Assert.Equal(
                [rect.Top, rect.Top + hr.ActualBorderTopWidth, rect.Bottom],
                ys);
            Assert.Equal(rect.Top + hr.ActualBorderTopWidth, rect.Bottom - hr.ActualBorderBottomWidth, 3);
        }

        [Theory]
        [InlineData("", 1.5)]
        [InlineData("border: 2px solid", 3.0)]
        [InlineData("border: 6px groove", 9.0)]
        [InlineData("border: 10px solid", 15.0)]
        [InlineData("height: 10px", 9.0)]
        [InlineData("border: 2px solid; height: 10px", 10.5)]
        public async Task NextInFlowSibling_StartsAtTheRulesBottom(string declaration, double expected)
        {
            // CSS 2.1 §8.3.1 with §10.5. The rule's own bottom was always right; what was wrong is that
            // the frame committed the NEXT sibling's offset against a value the rule had not resolved
            // yet, because a declared height only reached it later, in the layout epilogue. Resolving
            // the height inside the rule's own pass is what puts the two in the right order.
            var (root, _) = await LayoutAsync(declaration);
            var hr = LayoutHarness.FindById(root, "hr")!;
            var next = LayoutHarness.FindById(root, "next")!;

            Assert.Equal(expected, next.Location.Y - hr.Location.Y, 3);
        }

        [Theory]
        [InlineData("", 1.5)]
        [InlineData("border: 2px solid", 3.0)]
        [InlineData("border: 10px solid", 15.0)]
        [InlineData("height: 10px", 9.0)]
        public async Task ContainerHoldingOnlyARule_IsAsTallAsTheRule(string declaration, double expected)
        {
            // CSS 2.1 §10.6.3: a block container's content height runs to its last in-flow child's
            // bottom margin edge. A thick rule used to overflow its own container, which is the more
            // visible half of the defect - it breaks any container sized by its content.
            var (root, _) = await LayoutAsync(declaration);
            var parent = LayoutHarness.FindById(root, "wrap")!;

            Assert.Equal(expected, parent.ActualBottom - parent.Location.Y, 3);
        }

        [Fact]
        public async Task BorderlessHr_KeepsItsNominalHeight()
        {
            // The one case that still reaches the hard-coded 2: with neither a height nor a border there
            // is nothing to derive one from, and a zero-size box emits no fragment at all - it would be
            // absent from the fragment tree rather than merely invisible. Pinned so the constant is not
            // mistaken for the defect above and "cleaned up".
            var (root, container) = await LayoutAsync("border: none");
            var hr = LayoutHarness.FindById(root, "hr")!;

            Assert.Equal(2, FragmentPaintHarness.FragmentOf(container, hr).WholeBoxRect.Height, 3);
        }

        private static Task<(CssBox Root, HtmlContainerInt Container)> LayoutAsync(string declaration) =>
            LayoutHarness.LayoutAsync(
                "<!DOCTYPE html><html><body>"
                + $"<div id='wrap' style='margin: 0'><hr id='hr' style='margin: 0; {declaration}'></div>"
                + "<div id='next' style='margin: 0'>after</div>"
                + "</body></html>");
    }
}
