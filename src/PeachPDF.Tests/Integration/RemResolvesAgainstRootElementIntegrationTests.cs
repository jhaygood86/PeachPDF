using PeachPDF.Adapters;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.PdfSharpCore.Drawing;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// css-values-3 §5.1.2: <c>rem</c> resolves against the font size of the ROOT ELEMENT.
    /// <see cref="DerivedStyle.GetRemHeight"/> used to walk to the topmost <see cref="CssBox"/>, which is
    /// the container's synthetic root sitting ABOVE <c>&lt;html&gt;</c> and carrying no element, so its
    /// font size is always the resolver's default — every <c>rem</c> in a document that declares
    /// <c>html { font-size }</c> silently resolved against that default instead.
    /// <para>
    /// Every fixture here declares a non-default root font size, which is what makes the bug visible: with
    /// the root left at the UA default the wrong answer and the right one are the same number, which is why
    /// a 10,000-test suite stayed green through it. The expected values are written as literals rather than
    /// read back off the tree, so an assertion cannot agree with the bug.
    /// </para>
    /// </summary>
    public class RemResolvesAgainstRootElementIntegrationTests
    {
        // html { font-size: 32px } -> 32 x 0.75pt/px = 24pt (CSS Values & Units §6.2).
        private const double RootFontPt = 24.0;

        [Fact]
        public async Task RemFontSize_ResolvesAgainstTheRootElement_NotTheEngineDefault()
        {
            var root = await BuildAndLayout("""
                html { font-size: 32px; }
                body { font-size: 10pt; margin: 0; }
                """,
                "<div id='rem' style='font-size:1rem'>x</div>");

            Assert.Equal(RootFontPt, FindById(root, "rem")!.ActualFont.Size, 3);
        }

        [Fact]
        public async Task EmFontSize_StillResolvesAgainstTheParent()
        {
            // The contrast case: em is the PARENT's font size, so the two units must disagree here.
            // Without it, a `rem` that wrongly followed the parent would pass the test above whenever
            // the parent happened to be the root.
            var root = await BuildAndLayout("""
                html { font-size: 32px; }
                body { font-size: 10pt; margin: 0; }
                """,
                "<div id='em' style='font-size:1em'>x</div>");

            Assert.Equal(10.0, FindById(root, "em")!.ActualFont.Size, 3);
        }

        [Fact]
        public async Task RemLength_ResolvesAgainstTheRootElement()
        {
            // Not only font-size: every length that takes a rem reads the same basis.
            var root = await BuildAndLayout("""
                html { font-size: 32px; }
                body { font-size: 10pt; margin: 0; }
                """,
                "<div id='target' style='padding-top:2rem'>x</div>");

            Assert.Equal(2 * RootFontPt, FindById(root, "target")!.ActualPaddingTop, 3);
        }

        [Fact]
        public async Task RemOnTheRootElementItself_ResolvesAgainstTheInitialValue()
        {
            // css-values-3 §5.1.2's anti-recursion rule: a rem in the root element's OWN font-size
            // refers to the initial value, not to the size being computed.
            var root = await BuildAndLayout("html { font-size: 2rem; }", "<div id='x'>x</div>");

            var html = FindByTag(root, "html")!;
            Assert.Equal(2 * DefaultFontSizePt(root), html.ActualFont.Size, 3);
        }

        private static double DefaultFontSizePt(CssBox root) => root.ActualFont.Size;

        private static async Task<CssBox> BuildAndLayout(string css, string bodyHtml)
        {
            var html = $"<!DOCTYPE html><html><head><style>{css}</style></head><body>{bodyHtml}</body></html>";

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
            return container.Root!;
        }

        private static CssBox? FindById(CssBox box, string id)
        {
            if (box.HtmlTag?.TryGetAttribute("id", "") == id) return box;
            foreach (var child in box.Boxes)
            {
                if (FindById(child, id) is { } found) return found;
            }
            return null;
        }

        private static CssBox? FindByTag(CssBox box, string tag)
        {
            if (box.HtmlTag?.Name == tag) return box;
            foreach (var child in box.Boxes)
            {
                if (FindByTag(child, tag) is { } found) return found;
            }
            return null;
        }
    }
}
