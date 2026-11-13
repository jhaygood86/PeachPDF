using PeachPDF.Tests.TestSupport;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace PeachBrackets.Engine.Tests.Html.Core
{
    /// <summary>
    /// The root element establishes a block formatting context and its margins do not collapse (CSS 2.1 sections 9.4.1 and 8.3.1), and an absolutely
    /// positioned box with no positioned ancestor is placed against the initial containing block (section 10.1) - the page area, not the root element
    /// box, whose top used to carry the margin the body and its first child collapsed through it.
    /// </summary>
    public class RootElementMarginTests
    {
        private const string Document =
            "<!DOCTYPE html><html><body style='margin:8pt'><p id='p' style='margin:24pt 0 0'>text</p>"
            + "<div id='a' style='position:absolute;left:30pt;top:50pt;width:10pt;height:10pt'></div></body></html>";

        [Fact]
        public async Task TheHtmlBox_StartsAtTheTopOfThePageArea_NotBelowTheMarginsCollapsedThroughIt()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(Document);
            var html = LayoutHarness.Descendants(root).First(b => b.HtmlTag?.Name == "html");
            var body = LayoutHarness.Descendants(root).First(b => b.HtmlTag?.Name == "body");

            Assert.Equal(container.MarginTop, html.Location.Y, 3);
            // The collapsed 24pt (the paragraph's, larger than the body's 8pt) is the body's own margin inside the html box.
            Assert.Equal(container.MarginTop + 24, body.Location.Y, 3);
        }

        [Fact]
        public async Task AnAbsoluteBoxWithNoPositionedAncestor_IsPlacedFromThePageAreasCorner()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(Document);
            var a = LayoutHarness.FindById(root, "a")!;

            Assert.Equal(container.MarginLeft + 30, a.Location.X, 3);
            Assert.Equal(container.MarginTop + 50, a.Location.Y, 3);
        }

        [Fact]
        public async Task AnAbsoluteBox_InAPositionedAncestor_StillFollowsThatAncestor()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(
                "<!DOCTYPE html><html><body style='margin:8pt'><div id='c' style='position:relative;margin-top:40pt;height:100pt'>"
                + "<div id='a' style='position:absolute;left:5pt;top:7pt;width:10pt;height:10pt'></div></div></body></html>");
            var c = LayoutHarness.FindById(root, "c")!;
            var a = LayoutHarness.FindById(root, "a")!;

            Assert.Equal(c.Location.X + 5, a.Location.X, 3);
            Assert.Equal(c.Location.Y + 7, a.Location.Y, 3);
        }
    }
}
