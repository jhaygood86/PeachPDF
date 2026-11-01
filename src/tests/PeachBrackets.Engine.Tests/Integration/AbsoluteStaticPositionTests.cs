using PeachBrackets.Html.Dom;
using PeachPDF.Tests.TestSupport;
using System.Threading.Tasks;
using Xunit;

namespace PeachBrackets.Engine.Tests.Integration
{
    /// <summary>
    /// An absolutely positioned box whose offsets on an axis are both <c>auto</c> sits at its static position on that axis, where it would have been
    /// in flow (CSS 2.1 sections 10.3.7 and 10.6.4, CSS Positioned Layout 3 section 3.5): below the previous in-flow sibling and at the parent's content
    /// edge, not at the corner of its containing block. This covers a box in block flow.
    /// </summary>
    public class AbsoluteStaticPositionTests
    {
        private const string Style = "<style>body{margin:0} #c{position:relative;width:300pt;margin:0;padding:10pt 20pt} #p{margin:0;height:50pt} #a{position:absolute;width:20pt;height:10pt}</style>";

        private static async Task<(CssBox C, CssBox P, CssBox A)> Layout(string container)
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(Style + container));
            return (LayoutHarness.FindById(root, "c")!, LayoutHarness.FindById(root, "p")!, LayoutHarness.FindById(root, "a")!);
        }

        [Fact]
        public async Task AfterABlockSibling_SitsBelowItAtTheParentsContentEdge()
        {
            var (c, p, a) = await Layout("<div id='c'><div id='p'>para</div><div id='a'>x</div></div>");

            Assert.Equal(p.ActualBottom, a.Location.Y, 0.5);
            Assert.Equal(c.ClientLeft, a.Location.X, 0.5);
        }

        [Fact]
        public async Task AsTheFirstChild_SitsAtTheParentsContentTopLeft_NotItsPaddingCorner()
        {
            var (c, _, a) = await Layout("<div id='c'><div id='a'>x</div><div id='p'>para</div></div>");

            Assert.Equal(c.ClientTop, a.Location.Y, 0.5);
            Assert.Equal(c.ClientLeft, a.Location.X, 0.5);
            Assert.True(a.Location.X > c.Location.X + 15, "the 20pt padding is inside the static position");
        }

        [Fact]
        public async Task TheAbsoluteBoxsOwnMarginAddsToItsStaticPosition()
        {
            var (_, p, a) = await Layout("<div id='c'><div id='p'>para</div><div id='a' style='margin:7pt 0 0 5pt'>x</div></div>");

            Assert.Equal(p.ActualBottom + 7, a.Location.Y, 0.5);
        }

        [Fact]
        public async Task AnAxisWithAnOffsetSet_KeepsThatOffset_AndTheOtherStaysStatic()
        {
            var (c, p, a) = await Layout("<div id='c'><div id='p'>para</div><div id='a' style='left:30pt'>x</div></div>");

            // left is set: measured from the padding edge of the containing block; top is still static.
            Assert.Equal(c.Location.X + 30, a.Location.X, 0.5);
            Assert.Equal(p.ActualBottom, a.Location.Y, 0.5);
        }

        [Fact]
        public async Task BothOffsetsSet_AreUnchanged()
        {
            var (c, _, a) = await Layout("<div id='c'><div id='p'>para</div><div id='a' style='left:5pt;top:3pt'>x</div></div>");

            Assert.Equal(c.Location.X + 5, a.Location.X, 0.5);
            Assert.Equal(c.Location.Y + 3, a.Location.Y, 0.5);
        }

        [Fact]
        public async Task AmongInlineContent_IsLeftToTheInlineFlow()
        {
            // Not handled here: the static position is on a line. The box stays where it was placed before.
            var (c, _, a) = await Layout("<div id='c'>text<div id='a'>x</div></div>");

            Assert.Equal(c.Location.X, a.Location.X, 0.5);
        }
    }
}
