using PeachPDF.Adapters;
using PeachPDF.Html.Core;
using PeachPDF.PdfSharpCore;
using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.PdfSharpCore.Pdf;
using System.Text.RegularExpressions;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Regression coverage for the pre-existing HandleLinks bug found while implementing PDF bookmarks
    /// (issue #711): an anchor link's (`&lt;a href="#id"&gt;`) named destination was written as a
    /// `/FitV left` action with the target's Y offset passed as `left` (a horizontal parameter) instead
    /// of `/FitH top` (fits page width, positions vertically at `top`) - the correct pairing for
    /// "jump down to this Y". Fixed alongside the bookmark work since the new PdfOutline destinations
    /// use the same (now-corrected) PageAnchorResolver-driven math.
    /// </summary>
    public class AnchorLinkDestinationTests
    {
        [Fact]
        public async Task AnchorLink_NamedDestination_UsesFitHNotFitV()
        {
            var html = "<html><body><a href='#target'>go</a>" +
                       "<div style='height:2000px'></div>" +
                       "<h1 id='target'>Target</h1></body></html>";
            var result = await new PdfGenerator().GeneratePdf(html, PageSize.A4);

            var array = Assert.IsType<PdfArray>(result.PdfDocument.Catalog.Names.NameTree!.GetValue("target"));
            var literal = Assert.IsType<PdfLiteral>(array.Elements[0]);

            Assert.Contains("/FitH", literal.Value);
            Assert.DoesNotContain("/FitV", literal.Value);
        }

        // Regression test for a coordinate-space bug found post-implementation: the fix above (FitV ->
        // FitH) alone was not sufficient - the Y value passed to CreateFitHorizontally was still the
        // top-down (0-at-top, increasing downward) offset PageAnchorResolver computes, written directly
        // as /FitH's own "top" parameter, which PDF 32000-1 defines in bottom-up default user space (0 at
        // the page's bottom edge). A target near the top of its page therefore needs a Top value near the
        // FULL page height, not near 0 - verified against real PDF viewers (not just this repo's own
        // code), which is what caught this.
        [Fact]
        public async Task AnchorLink_NamedDestination_TopIsBottomUp_NearPageHeightForATargetNearThePageTop()
        {
            var html = "<html><body><a href='#target'>go</a>" +
                       "<div style='height:1200px'></div>" +
                       "<h1 id='target' style='break-before:page'>Target</h1></body></html>";
            var result = await new PdfGenerator().GeneratePdf(html, PageSize.A4);

            var array = Assert.IsType<PdfArray>(result.PdfDocument.Catalog.Names.NameTree!.GetValue("target"));
            var literal = Assert.IsType<PdfLiteral>(array.Elements[0]);

            var match = Regex.Match(literal.Value, @"/FitH\s+([\d.]+)");
            Assert.True(match.Success, $"Expected a /FitH value in '{literal.Value}'");
            var top = double.Parse(match.Groups[1].Value);

            var pageHeight = (double)result.PdfDocument.Pages[result.PdfDocument.Pages.Count - 1].Height;
            Assert.True(top > pageHeight * 0.8,
                $"Target starts its own fresh page (break-before:page), so its bottom-up Top ({top}) should be near the full page height ({pageHeight}), not near 0.");
        }

        // Regression test for issue #801: an `<a href="#">` (an empty fragment, a common JS-driven
        // no-op link pattern in real-world HTML) has Href.Length == 1, so LinkElementData.AnchorId
        // evaluates to string.Empty rather than a real id. HandleLinks's ResolveAnchorTarget local
        // passed that empty string straight to HtmlContainer.GetElementRectangle, which asserts its
        // elementId argument is non-null/non-empty and threw ArgumentNullException("elementId"),
        // crashing PDF generation for any document containing such a link.
        [Fact]
        public async Task AnchorLink_EmptyFragment_DoesNotThrowAndAddsNoAnnotation()
        {
            var html = "<html><body><a href='#'>no-op</a></body></html>";
            var result = await new PdfGenerator().GeneratePdf(html, PageSize.A4);

            Assert.Equal(0, result.PdfDocument.Pages[0].Annotations.Count);
        }

        // PdfGenerator.HandleLinks always calls the internal HtmlContainer.GetLinks(bookmarkBoxes)
        // overload now (so bookmark collection rides along on the same walk, see
        // DomUtils.GetAllLinkAndBookmarkBoxes), leaving the public no-arg GetLinks() only reachable by
        // a caller using HtmlContainer directly (outside PdfGenerator) - covered here directly since
        // no generator-level test exercises it anymore.
        [Fact]
        public async Task PublicGetLinksNoArgOverload_StillReturnsLinkData()
        {
            var adapter = new PdfSharpAdapter();
            var container = new HtmlContainer(adapter);
            await container.SetHtml("<html><body><a href='https://example.com'>go</a></body></html>");
            container.MaxSize = new XSize(600, 0);
            using var measure = XGraphics.CreateMeasureContext(new XSize(600, 800), XGraphicsUnit.Point, XPageDirection.Downwards);
            await container.PerformLayout(measure);

            var links = container.GetLinks();

            var link = Assert.Single(links);
            Assert.Equal("https://example.com/", link.Href);
        }
    }
}
