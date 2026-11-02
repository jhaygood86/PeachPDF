using PeachPDF.Tests.TestSupport;
using System.Threading.Tasks;
using Xunit;

namespace PeachBrackets.Engine.Tests.Integration
{
    /// <summary>
    /// An <c>@page</c> margin in <c>rem</c> (or <c>em</c> with no <c>@page</c> font) resolves against the real
    /// <c>&lt;html&gt;</c> element's font size (CSS Values 4 section 6.1.1), not the engine default.
    /// </summary>
    public class PageMarginRootFontSizeTests
    {
        private static async Task<(double Margin, double RootFont, double BodyRem)> Run(string css)
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                $"<!DOCTYPE html><html id='h'><head><style>{css}</style></head><body><p id='p'>content</p></body></html>");
            var html = LayoutHarness.FindById(root, "h")!;
            return (container.MarginTop, html.ActualFont.Size, LayoutHarness.FindById(root, "p")!.GetRemHeight());
        }

        [Fact]
        public async Task RemMargin_UsesTheRootElementsFontSize()
        {
            var (margin, rootFont, _) = await Run("html { font-size: 32px } @page { margin-top: 1rem }");
            Assert.Equal(24, rootFont, 0.01);
            Assert.Equal(24, margin, 0.01);
        }

        [Fact]
        public async Task EmMargin_WithNoPageFontSize_UsesTheRootElementsFontSize()
        {
            var (margin, _, _) = await Run("html { font-size: 32px } @page { margin-top: 2em }");
            Assert.Equal(48, margin, 0.01);
        }

        [Fact]
        public async Task TheRootFontIsNotPoisoned_RemInTheContentStillResolves()
        {
            var (_, rootFont, bodyRem) = await Run("html { font-size: 32px } @page { margin-top: 1rem }");
            Assert.Equal(24, rootFont, 0.01);
            Assert.Equal(24, bodyRem, 0.01);
        }

        [Fact]
        public async Task APageFontSize_StillWinsForEm()
        {
            var (margin, _, _) = await Run("html { font-size: 32px } @page { font-size: 10pt; margin-top: 2em }");
            Assert.Equal(20, margin, 0.01);
        }

        [Fact]
        public async Task ADefaultRootFont_LeavesMarginsUnchanged()
        {
            var (_, container) = await LayoutHarness.LayoutAsync(
                "<!DOCTYPE html><html><head><style>@page { margin-top: 2pt }</style></head><body>x</body></html>");
            Assert.Equal(2, container.MarginTop, 0.01);
        }
    }
}
