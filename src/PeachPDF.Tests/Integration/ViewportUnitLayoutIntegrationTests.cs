using PeachPDF.Adapters;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Utils;
using PeachPDF.PdfSharpCore.Drawing;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// End-to-end coverage for viewport-relative units (<c>vw</c>/<c>vh</c>/<c>vi</c>/<c>vb</c>/
    /// <c>vmin</c>/<c>vmax</c>, and their <c>sv*</c>/<c>lv*</c>/<c>dv*</c> variants) and <c>ch</c>
    /// (issue #615). Mirrors <see cref="ContainerQueryLayoutIntegrationTests"/>'s harness idiom (bare
    /// <c>HtmlContainerInt</c> + <c>SetHtml</c>/<c>PerformLayout</c>, no <c>PdfGenerator</c>), whose
    /// 800x600pt <see cref="HtmlContainerInt.PageSize"/> is the "viewport" every expectation below is
    /// computed against.
    /// </summary>
    public class ViewportUnitLayoutIntegrationTests
    {
        [Theory]
        [InlineData("width: 50vw", 400d)]
        [InlineData("height: 50vh", 300d)]
        [InlineData("width: 25vmin", 150d)]  // 25% of min(800, 600) = 600
        [InlineData("width: 25vmax", 200d)]  // 25% of max(800, 600) = 800
        public async Task ViewportUnit_ResolvesAgainstThePageBox(string declaration, double expectedPt)
        {
            var isHeight = declaration.StartsWith("height");
            // Only set the OTHER axis explicitly - setting the same property under test a second time
            // would just make the later declaration win the cascade, testing nothing.
            var complementary = isHeight ? "width: 10px;" : "height: 10px;";
            var html = Html($"#target {{ {declaration}; {complementary} }}", "<div id='target'></div>");

            var root = await BuildRoot(html);
            var target = DomUtils.GetBoxById(root, "target");
            Assert.NotNull(target);

            var actual = isHeight ? target!.ActualBoxSizingHeight : target!.ActualBoxSizingWidth;
            Assert.Equal(expectedPt, actual, 1);
        }

        [Theory]
        [InlineData("50svw", 400d)]
        [InlineData("50lvw", 400d)]
        [InlineData("50dvw", 400d)]
        [InlineData("50svh", 300d)]
        [InlineData("50lvh", 300d)]
        [InlineData("50dvh", 300d)]
        public async Task SmallLargeDynamicViewportUnit_ResolvesIdenticallyToThePlainUnit(string widthValue, double expectedPt)
        {
            // A PDF page has no scrollbar or dynamic browser chrome to distinguish these by - all three
            // variants resolve identically to the plain unit here.
            var isHeight = widthValue.EndsWith('h');
            var declaration = isHeight ? $"height: {widthValue}" : $"width: {widthValue}";
            var complementary = isHeight ? "width: 10px;" : "height: 10px;";
            var html = Html($"#target {{ {declaration}; {complementary} }}", "<div id='target'></div>");

            var root = await BuildRoot(html);
            var target = DomUtils.GetBoxById(root, "target");
            Assert.NotNull(target);

            var actual = isHeight ? target!.ActualBoxSizingHeight : target!.ActualBoxSizingWidth;
            Assert.Equal(expectedPt, actual, 1);
        }

        // ── vi/vb follow the ROOT element's own writing-mode (issue #795) ─
        // A vertical-rl root's own inline axis is the page's physical height and block axis is the
        // page's physical width - the inverse of a horizontal-tb root's. vw/vh must stay physical
        // regardless (always width/height respectively); only vi/vb rotate onto the swapped axis. The
        // target is pinned to horizontal-tb so its OWN layout stays on the ordinary, well-tested
        // physical-width path - only the document root's own writing-mode is under test.

        [Fact]
        public async Task ViewportUnit_LogicalForm_FollowsTheRootElementsOwnWritingMode()
        {
            var html = "<!DOCTYPE html><html style=\"writing-mode: vertical-rl\"><head><style>" +
                "#target { writing-mode: horizontal-tb; width: 50vi; height: 50vb; }" +
                "</style></head><body><div id='target'></div></body></html>";

            var root = await BuildRoot(html);
            var target = DomUtils.GetBoxById(root, "target");
            Assert.NotNull(target);

            // vi tracks the page's own height axis (600pt -> 50% = 300pt) and vb tracks the page's own
            // width axis (800pt -> 50% = 400pt) - swapped from vw/vh's own values below, proving vi/vb
            // actually follow the root element's writing-mode rather than aliasing vw/vh.
            Assert.Equal(300d, target!.ActualBoxSizingWidth, 1);
            Assert.Equal(400d, target!.ActualBoxSizingHeight, 1);
        }

        [Fact]
        public async Task ViewportUnit_PhysicalForm_StaysPhysicalRegardlessOfTheRootElementsWritingMode()
        {
            var html = "<!DOCTYPE html><html style=\"writing-mode: vertical-rl\"><head><style>" +
                "#target { writing-mode: horizontal-tb; width: 50vw; height: 50vh; }" +
                "</style></head><body><div id='target'></div></body></html>";

            var root = await BuildRoot(html);
            var target = DomUtils.GetBoxById(root, "target");
            Assert.NotNull(target);

            Assert.Equal(400d, target!.ActualBoxSizingWidth, 1);
            Assert.Equal(300d, target!.ActualBoxSizingHeight, 1);
        }

        [Fact]
        public async Task ViewportUnit_ResolvesInsideCalc()
        {
            var html = Html(
                "#target { width: calc(50vw + 10px); height: 10px; }",
                "<div id='target'></div>");

            var root = await BuildRoot(html);
            var target = DomUtils.GetBoxById(root, "target");
            Assert.NotNull(target);
            // 50% of the 800pt page width (400pt) + 10px (7.5pt) = 407.5pt.
            Assert.Equal(407.5d, target!.ActualBoxSizingWidth, 1);
        }

        [Fact]
        public async Task ViewportUnit_ForFontSize_ResolvesForTextContent()
        {
            // Unlike cqw (see ContainerQueryLayoutIntegrationTests's own font-size case and
            // .claude/accepted-gaps/font-size-container-relative-units-resolve-to-zero-for-text-content.md),
            // a viewport unit needs no ancestor container, so it isn't gated by the pre-layout font-caching
            // order issue - it resolves correctly even for a box with real text content.
            var html = Html("#target { font-size: 10vw; }", "<p id='target'>text</p>");

            var root = await BuildRoot(html);
            var target = DomUtils.GetBoxById(root, "target");
            Assert.NotNull(target);
            // 10% of the 800pt page width.
            Assert.Equal(80d, target!.ActualFont.Size, 1);
        }

        [Fact]
        public async Task Ch_ResolvesAsHalfEm()
        {
            var html = Html(
                "#target { font-size: 20pt; width: 10ch; height: 10px; }",
                "<div id='target'></div>");

            var root = await BuildRoot(html);
            var target = DomUtils.GetBoxById(root, "target");
            Assert.NotNull(target);
            // 10 * 0.5 * 20pt = 100pt.
            Assert.Equal(100d, target!.ActualBoxSizingWidth, 1);
        }

        [Fact]
        public async Task Ch_ResolvesInsideCalc()
        {
            var html = Html(
                "#target { font-size: 20pt; width: calc(10ch + 5px); height: 10px; }",
                "<div id='target'></div>");

            var root = await BuildRoot(html);
            var target = DomUtils.GetBoxById(root, "target");
            Assert.NotNull(target);
            // 10 * 0.5 * 20pt (100pt) + 5px (3.75pt) = 103.75pt.
            Assert.Equal(103.75d, target!.ActualBoxSizingWidth, 1);
        }

        // ─── Harness ──────────────────────────────────────────────────────────

        private static string Html(string css, string body) =>
            $"<!DOCTYPE html><html><head><style>{css}</style></head><body>{body}</body></html>";

        private static async Task<CssBox> BuildRoot(string html)
        {
            var adapter = new PdfSharpAdapter { PixelsPerPoint = 1.0 };
            var container = new HtmlContainerInt(adapter);

            var size = new XSize(800, 600);
            container.PageSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);
            container.MaxSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);

            await container.SetHtml(html, null);

            var measure = XGraphics.CreateMeasureContext(size, XGraphicsUnit.Point, XPageDirection.Downwards);
            using var graphics = new GraphicsAdapter(adapter, measure, 1.0);
            await container.PerformLayout(graphics);

            Assert.NotNull(container.Root);
            return container.Root!;
        }
    }
}
