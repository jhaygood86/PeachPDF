using PeachDrawing.PDF.Engine;
using System.IO;
using System.Linq;
using System.Text;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Integration coverage for CSS Generated Content Module Level 3's bookmark properties (issue
    /// #711) - asserts directly on the built <see cref="PdfOutline"/>/<see cref="PdfOutlineCollection"/>
    /// tree (title/nesting/open-state/destination), not a content-stream substring, per this repo's
    /// testing convention for anything touching PDF-writing internals.
    /// </summary>
    public class BookmarkOutlineTests
    {
        [Fact]
        public async Task NoHeadings_ProducesNoOutline_AndNoUseOutlinesPageMode()
        {
            var result = await new PdfGenerator().GeneratePdf("<html><body><p>Hello</p></body></html>", PageSize.A4);

            Assert.Empty(result.Inspect().Outlines);

            using var stream = new MemoryStream();
            result.Save(stream);
            var bytes = Encoding.Latin1.GetString(stream.ToArray());
            Assert.DoesNotContain("/UseOutlines", bytes);
        }

        [Fact]
        public async Task DefaultHeadings_NestByLevel_InDocumentOrder()
        {
            var html = "<html><body>" +
                       "<h1>Chapter 1</h1><h2>Section 1.1</h2><h2>Section 1.2</h2>" +
                       "<h1>Chapter 2</h1>" +
                       "</body></html>";
            var result = await new PdfGenerator().GeneratePdf(html, PageSize.A4);

            var top = result.Inspect().Outlines.ToList();
            Assert.Equal(new[] { "Chapter 1", "Chapter 2" }, top.Select(o => o.Title));

            var chapter1Children = top[0].Children.ToList();
            Assert.Equal(new[] { "Section 1.1", "Section 1.2" }, chapter1Children.Select(o => o.Title));
            Assert.Empty(top[1].Children);
        }

        [Fact]
        public async Task BookmarkLevelNone_SuppressesHeading()
        {
            var html = "<html><body>" +
                       "<h1 style='bookmark-level:none'>Skip</h1><h1>Keep</h1>" +
                       "</body></html>";
            var result = await new PdfGenerator().GeneratePdf(html, PageSize.A4);

            var top = result.Inspect().Outlines.ToList();
            Assert.Equal(new[] { "Keep" }, top.Select(o => o.Title));
        }

        [Fact]
        public async Task CustomBookmarkLevel_OverridesDefaultNesting()
        {
            // h3's own default (level 3) is overridden down to 2, so it nests directly under the h1
            // even though no h2 ever appeared - the level stack, not DOM nesting, drives the tree.
            var html = "<html><body>" +
                       "<h1>A</h1><h3 style='bookmark-level:2'>B</h3>" +
                       "</body></html>";
            var result = await new PdfGenerator().GeneratePdf(html, PageSize.A4);

            var top = result.Inspect().Outlines.ToList();
            var a = Assert.Single(top);
            Assert.Equal("A", a.Title);
            var b = Assert.Single(a.Children);
            Assert.Equal("B", b.Title);
        }

        [Fact]
        public async Task BookmarkLabel_DefaultsToElementOwnText()
        {
            var result = await new PdfGenerator().GeneratePdf("<html><body><h1>Hello World</h1></body></html>", PageSize.A4);

            Assert.Equal("Hello World", result.Inspect().Outlines.Single().Title);
        }

        [Fact]
        public async Task BookmarkLabel_LiteralAndAttr_AreConcatenated()
        {
            var html = "<html><body>" +
                       "<h1 data-title='Intro' style='bookmark-label:\"Custom: \" attr(data-title)'>Ignored text</h1>" +
                       "</body></html>";
            var result = await new PdfGenerator().GeneratePdf(html, PageSize.A4);

            Assert.Equal("Custom: Intro", result.Inspect().Outlines.Single().Title);
        }

        [Fact]
        public async Task BookmarkLabel_Counter_IsFormatted()
        {
            var html = "<html><body style='counter-reset:ch'>" +
                       "<h1 style='counter-increment:ch;bookmark-label:\"Ch. \" counter(ch)'>Ignored</h1>" +
                       "</body></html>";
            var result = await new PdfGenerator().GeneratePdf(html, PageSize.A4);

            Assert.Equal("Ch. 1", result.Inspect().Outlines.Single().Title);
        }

        [Fact]
        public async Task BookmarkState_DefaultsOpen_ClosedIsRespected()
        {
            var html = "<html><body>" +
                       "<h1>Open by default</h1><h2>Child</h2><h1 style='bookmark-state:closed'>Closed</h1><h2>Hidden child</h2>" +
                       "</body></html>";
            var result = await new PdfGenerator().GeneratePdf(html, PageSize.A4);

            var top = result.Inspect().Outlines.ToList();
            Assert.True(top[0].Opened);
            Assert.False(top[1].Opened);
        }

        // Regression test for a coordinate-space bug found post-implementation: BookmarkOutlineBuilder
        // wrote the top-down (0-at-top, increasing downward) Y PageAnchorResolver computes directly into
        // /FitH's own "top" parameter, which PDF 32000-1 defines in bottom-up default user space (0 at
        // the page's bottom edge) - landing every destination far from its heading (verified against real
        // PDF viewers, not just this repo's own code). The fix flips via the destination page's own
        // height. This test would have caught it: with the bug, an EARLIER (visually higher) heading's
        // unflipped Top was numerically SMALLER than a LATER (visually lower) heading's - the wrong way
        // around for bottom-up space, where "higher on the page" must mean a LARGER Y.
        [Fact]
        public async Task BookmarkTarget_Self_TopIsBottomUp_EarlierHeadingHasLargerTopThanLaterHeadingOnSamePage()
        {
            var html = "<html><body>" +
                       "<h1>First</h1><p style='height:400px'></p><h1>Second</h1>" +
                       "</body></html>";
            var result = await new PdfGenerator().GeneratePdf(html, PageSize.A4);

            var top = result.Inspect().Outlines.ToList();
            var first = top.Single(o => o.Title == "First");
            var second = top.Single(o => o.Title == "Second");

            Assert.Equal(first.DestinationPageIndex, second.DestinationPageIndex);
            Assert.True(first.DestinationTop > second.DestinationTop,
                $"First (visually higher) should have a larger bottom-up Top than Second (visually lower), got First.Top={first.DestinationTop}, Second.Top={second.DestinationTop}");
            var pageHeight = PdfFile.Height(result.Inspect().Pages[first.DestinationPageIndex]);
            Assert.InRange(first.DestinationTop!.Value, 0, pageHeight);
            Assert.InRange(second.DestinationTop!.Value, 0, pageHeight);
        }

        // Regression test for a second bug the coordinate-flip fix above uncovered: a display:none
        // heading still has a CssBox (just never painted), so its default self target would otherwise
        // resolve via CommonUtils.GetFirstValueOrDefault's Bounds fallback to a nonsense page-0/Y-0
        // destination rather than the documented "leave the outline with neither" behavior. An earlier
        // attempt at this check (`box.Rectangles.Count == 0`) was itself wrong - Rectangles is commonly
        // empty for perfectly ordinary, visible block boxes too, not just unrendered ones - so this test
        // exists specifically to keep the real fix (an ActualDisplay check) from regressing back to that.
        [Fact]
        public async Task BookmarkTarget_Self_DisplayNoneHeading_LeavesDestinationUnset()
        {
            var html = "<html><body><h1 style='display:none'>Hidden</h1></body></html>";
            var result = await new PdfGenerator().GeneratePdf(html, PageSize.A4);

            var outline = result.Inspect().Outlines.Single();
            Assert.Equal("Hidden", outline.Title);
            Assert.False(outline.HasDestinationPage);
            Assert.Null(outline.Url);
        }

        [Fact]
        public async Task BookmarkTarget_Self_PointsAtOwnPage()
        {
            var result = await new PdfGenerator().GeneratePdf("<html><body><h1>Title</h1></body></html>", PageSize.A4);

            var outline = result.Inspect().Outlines.Single();
            Assert.Equal(0, outline.DestinationPageIndex);
            Assert.Null(outline.Url);
        }

        [Fact]
        public async Task BookmarkTarget_UrlFragment_PointsAtInDocumentElement()
        {
            var html = "<html><body>" +
                       "<h1 style='-peachpdf-bookmark-target:url(#other)'>Title</h1>" +
                       "<div id='other'>Target</div>" +
                       "</body></html>";
            var result = await new PdfGenerator().GeneratePdf(html, PageSize.A4);

            var outline = result.Inspect().Outlines.Single();
            Assert.True(outline.HasDestinationPage);
            Assert.Null(outline.Url);
        }

        [Fact]
        public async Task BookmarkTarget_AttrHrefExternalUrl_SetsOutlineUrl()
        {
            var html = "<html><body>" +
                       "<h1 data-href='https://example.com' style='-peachpdf-bookmark-target:attr(data-href)'>Title</h1>" +
                       "</body></html>";
            var result = await new PdfGenerator().GeneratePdf(html, PageSize.A4);

            var outline = result.Inspect().Outlines.Single();
            // Resolved via HtmlContainer.ResolveHref, same as an equivalent <a href> - which normalizes
            // a bare-authority URL with an added trailing slash.
            Assert.Equal("https://example.com/", outline.Url);
            Assert.False(outline.HasDestinationPage);
        }

        [Fact]
        public async Task BookmarkTarget_RelativeExternalUrl_IsResolvedAgainstBaseUri()
        {
            // -peachpdf-bookmark-target's external-URL branch resolves through HtmlContainer.ResolveHref
            // - the same base-URI resolution an equivalent <a href="other.html"> already gets - rather
            // than being written raw and unresolvable by a reader for anything but an already-absolute URL.
            var html = "<html><head><base href='https://example.com/docs/'></head><body>" +
                       "<h1 style='-peachpdf-bookmark-target:url(other.html)'>Title</h1>" +
                       "</body></html>";
            var result = await new PdfGenerator().GeneratePdf(html, PageSize.A4);

            Assert.Equal("https://example.com/docs/other.html", result.Inspect().Outlines.Single().Url);
        }

        [Theory]
        [InlineData("-prince-bookmark-level:1")]
        [InlineData("bookmark-level:1")]
        public async Task PrinceAliasSpelling_ProducesSameOutlineAsCanonical(string levelDeclaration)
        {
            var html = $"<html><body><p style='{levelDeclaration}'>Aliased</p></body></html>";
            var result = await new PdfGenerator().GeneratePdf(html, PageSize.A4);

            Assert.Equal("Aliased", result.Inspect().Outlines.Single().Title);
        }

        [Fact]
        public async Task BareBookmarkTargetAlias_BehavesLikePeachPdfBookmarkTarget()
        {
            var html = "<html><body>" +
                       "<h1 data-href='https://example.com' style='bookmark-target:attr(data-href)'>Title</h1>" +
                       "</body></html>";
            var result = await new PdfGenerator().GeneratePdf(html, PageSize.A4);

            Assert.Equal("https://example.com/", result.Inspect().Outlines.Single().Url);
        }
    }
}
