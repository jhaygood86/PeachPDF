using PeachPDF.Adapters;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.PdfSharpCore.Drawing;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Layer K (Tier 0) of mixed page orientation/size support: a <c>position: fixed</c> box's own
    /// percentage width/height must resolve against the page area (CSS2.1 §10.1: the initial
    /// containing block), the same basis <c>CssBox.CommitBlockChildOffset</c>'s fixed branch already
    /// uses for <c>left</c>/<c>top</c> - previously it fell through to the ordinary DOM
    /// <c>ContainingBlock</c> chain, exactly like a <c>position: static</c> box, which is wrong
    /// independent of anything to do with mixed page sizes (a pre-existing bug found while designing
    /// that feature). Same harness convention as <see cref="AbsolutePositioningIntegrationTests"/>,
    /// whose analogous absolute-positioning fix this mirrors.
    /// </summary>
    public class FixedPositionPercentageSizeIntegrationTests
    {
        [Fact]
        public async Task FixedPercentWidth_ResolvesAgainstPageArea_NotItsNarrowStaticAncestor()
        {
            // The fixed box's ContainingBlock (nearest in-flow block ancestor) is a 120pt-wide div -
            // before the fix, `width: 50%` resolved against that (60pt). It must resolve against the
            // 595pt-wide page instead (297.5pt).
            var (root, _) = await BuildAndLayout(Wrap(
                "<div style='width:120pt;'>" +
                "<div id='fixed' style='position:fixed; width:50%;'></div>" +
                "</div>"));

            var fixedBox = FindById(root, "fixed")!;
            Assert.Equal(297.5, fixedBox.Size.Width, 1.5);
        }

        [Fact]
        public async Task FixedPercentHeight_ResolvesAgainstPageArea_NotItsShortStaticAncestor()
        {
            // Same for height: the ancestor's 80pt height must not be the basis - the 842pt page is.
            var (root, _) = await BuildAndLayout(Wrap(
                "<div style='height:80pt;'>" +
                "<div id='fixed' style='position:fixed; height:50%;'></div>" +
                "</div>"));

            var fixedBox = FindById(root, "fixed")!;
            Assert.Equal(421, fixedBox.ActualHeight, 1.5);
        }

        [Fact]
        public async Task FixedPercentWidthAndHeight_MatchTheBoxsOwnPercentageOffsetBasis()
        {
            // Regression guard tying the two together: left/top (already page-area-based) and
            // width/height (newly fixed) must now agree on the same basis for the same box.
            var (root, _) = await BuildAndLayout(Wrap(
                "<div id='fixed' style='position:fixed; left:10%; top:10%; width:20%; height:20%;'></div>"));

            var fixedBox = FindById(root, "fixed")!;
            Assert.Equal(59.5, fixedBox.Location.X, 1.5);  // 10% of 595
            Assert.Equal(84.2, fixedBox.Location.Y, 1.5);  // 10% of 842
            Assert.Equal(119, fixedBox.Size.Width, 1.5);   // 20% of 595
            Assert.Equal(168.4, fixedBox.ActualHeight, 1.5); // 20% of 842
        }

        [Fact]
        public async Task StaticPercentWidth_StillResolvesAgainstItsOwnContainingBlock()
        {
            // Regression guard: an ordinary (non-fixed) box's percentage resolution is unaffected.
            var (root, _) = await BuildAndLayout(Wrap(
                "<div style='width:120pt;'>" +
                "<div id='block' style='width:50%;'></div>" +
                "</div>"));

            var block = FindById(root, "block")!;
            Assert.Equal(60, block.Size.Width, 1.5);
        }

        private static string Wrap(string body) =>
            $"<!DOCTYPE html><html><head><style>body,div{{margin:0}}</style></head><body>{body}</body></html>";

        private static CssBox? FindById(CssBox box, string id)
        {
            if (string.Equals(box.HtmlTag?.TryGetAttribute("id", ""), id, System.StringComparison.OrdinalIgnoreCase))
                return box;

            foreach (var child in box.Boxes)
            {
                var found = FindById(child, id);
                if (found != null) return found;
            }

            return null;
        }

        private static async Task<(CssBox root, HtmlContainerInt container)> BuildAndLayout(string html)
        {
            var adapter = new PdfSharpAdapter { PixelsPerPoint = 1.0 };
            var container = new HtmlContainerInt(adapter);
            await container.SetHtml(html, null);

            var size = new XSize(595, 842);
            container.PageSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);
            container.MaxSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);

            var measure = XGraphics.CreateMeasureContext(size, XGraphicsUnit.Point, XPageDirection.Downwards);
            using var graphics = new GraphicsAdapter(adapter, measure, 1.0);
            await container.PerformLayout(graphics);

            Assert.NotNull(container.Root);
            return (container.Root!, container);
        }
    }
}
