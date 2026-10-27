using PeachBrackets.Html.Dom;
using PeachPDF.Tests.TestSupport;
using System.Threading.Tasks;

namespace PeachBrackets.Engine.Tests.Integration
{
    /// <summary>An iframe keeps the 2px inset border a browser gives it, outside its 300x150px content box.</summary>
    public class IframeBorderTests
    {
        [Fact]
        public async Task Iframe_InBlockFlow_IsLaidOutWithItsDefaultBorder()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap("<iframe id='i'></iframe>"));

            var frame = LayoutHarness.FindById(root, "i")!;
            Assert.Equal(1.5, frame.ActualBorderLeftWidth, 3);
            Assert.Equal(1.5, frame.ActualBorderBottomWidth, 3);
        }

        [Fact]
        public async Task Iframe_AsAGridItem_IsLaidOutWithItsDefaultBorder()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div style='display:grid;grid-template-columns:100pt 100pt;width:300pt'><iframe id='i'></iframe></div>"));

            var frame = LayoutHarness.FindById(root, "i")!;
            Assert.Equal(228, frame.ActualWidth, 1);
            Assert.Equal(115.5, frame.ActualHeight, 1);
        }

        [Fact]
        public async Task Iframe_WithAuthorBorderNone_HasNoBorder()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap("<iframe id='i' style='border:0'></iframe>"));

            var frame = LayoutHarness.FindById(root, "i")!;
            Assert.Equal(0, frame.ActualBorderLeftWidth);
        }
    }
}
