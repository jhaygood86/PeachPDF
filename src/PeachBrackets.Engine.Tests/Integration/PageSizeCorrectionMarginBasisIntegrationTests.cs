using PeachPDF.Tests.TestSupport;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Issue #582: when a document's own base <c>@page { size: ... }</c> differs from the configured
    /// <see cref="PdfGenerateConfig"/> page size, <c>DomParser.CascadeApplyPageStyles</c> corrects
    /// <c>HtmlContainerInt.PageSize</c> IN PLACE (before the expensive cascade runs) and re-resolves once
    /// more, rather than <c>PdfGenerator</c> discarding the whole parse and calling
    /// <c>SetContent</c>/<c>SetHtml</c> a second time. A percentage <c>@page</c> margin (css-page-3 §7.1)
    /// is the fixture that proves this actually happened: it must resolve against the CSS-corrected page
    /// width, not the originally-configured one. Goes through
    /// <see cref="PdfGeneratorLayoutHarness.LayoutAsync"/> (the production page-size-resolution path), not
    /// a manually pre-set <c>HtmlContainerInt.PageSize</c> - the existing
    /// PageSizeUnitIntegrationTests/PageMarginUnitConsistencyIntegrationTests fixtures never exercise the
    /// correction path at all, since they set the correct size before the one <c>SetHtml</c> call they
    /// make.
    /// </summary>
    public class PageSizeCorrectionMarginBasisIntegrationTests
    {
        [Fact]
        public async Task PercentageMargin_ResolvesAgainstFinalCssPageWidth_NotConfiguredWidth()
        {
            var config = new PdfGenerateConfig
            {
                ManualPageWidth = 400,
                ManualPageHeight = 600,
            };
            config.SetMargins(0);

            const string html = """
                <!DOCTYPE html><html><head><style>
                @page { size: 300pt 500pt; margin: 10%; }
                body { margin: 0; }
                </style></head><body><p>content</p></body></html>
                """;

            var (_, container) = await PdfGeneratorLayoutHarness.LayoutAsync(html, config);

            // The correction actually happened: CssPageSize is the declared 300x500pt sheet.
            Assert.NotNull(container.CssPageSize);
            Assert.Equal(300.0, container.CssPageSize!.Value.Width, 3);
            Assert.Equal(500.0, container.CssPageSize!.Value.Height, 3);

            // 10% must resolve against the CSS-corrected 300pt page width (30pt), not the
            // originally-configured 400pt sheet (40pt) - the wrong-answer value a shortcut that skipped
            // the correction (or applied it after margins were already resolved) would produce.
            // PixelsPerInch defaults to 72, so pixelsPerPoint == 1 and layout units equal points.
            Assert.Equal(30.0, container.MarginLeft, 3);
            Assert.Equal(30.0, container.MarginTop, 3);
            Assert.Equal(30.0, container.MarginRight, 3);
            Assert.Equal(30.0, container.MarginBottom, 3);
        }

        [Fact]
        public async Task EmMargin_ResolvesAgainstPageFontSize_RegardlessOfSizeCorrection()
        {
            // Regression guard alongside the % case above: an em @page margin (issue #162 basis - the
            // page context's own font-size, not root) must keep resolving identically whether or not a
            // page-size correction pass ran, since ApplyPageStylesOnce's em/rem basis doesn't depend on
            // PageSize.Width at all - only % does. font-size: 20pt => 2em = 40pt.
            var config = new PdfGenerateConfig
            {
                ManualPageWidth = 400,
                ManualPageHeight = 600,
            };
            config.SetMargins(0);

            const string html = """
                <!DOCTYPE html><html><head><style>
                @page { size: 300pt 500pt; font-size: 20pt; margin-left: 2em; }
                body { margin: 0; }
                </style></head><body><p>content</p></body></html>
                """;

            var (_, container) = await PdfGeneratorLayoutHarness.LayoutAsync(html, config);

            Assert.NotNull(container.CssPageSize);
            Assert.Equal(40.0, container.MarginLeft, 3);
        }
    }
}
