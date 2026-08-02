using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Entities;
using System.Collections.Generic;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Direct unit tests for <see cref="MarginBoxRenderer.ResolveNamedString"/>'s page-boundary matching -
    /// exercised directly (not through full HTML->PDF generation) since it's a pure function over an
    /// explicit <see cref="NamedString"/> list and a page-index lookup, per this repo's testing
    /// conventions. <see cref="PageIndexOf"/> stands in for production's <c>htmlContainer.SlotStartingAt</c>
    /// with the same non-variable-geometry division plus <see cref="HtmlContainerInt.PageBoundaryEpsilon"/>.
    ///
    /// Regression coverage for three compounding bugs found while investigating wrong running-header
    /// pairing on real content pages of css4.pub's Icelandic dictionary:
    ///  1. A page's true content range is [pageY + MarginTop, pageY + MarginTop + pageHeight), not
    ///     [pageY, pageY + pageHeight), since content's own coordinate system starts at MarginTop, not 0
    ///     (see PdfGenerator.AddPdfPages, which now adds +container.MarginTop when computing pageY).
    ///  2. A NamedString's Y and the page boundary used to be compared via independent accumulation paths
    ///     (row/column layout math vs. per-page scroll-offset accumulation), so a value genuinely meant to
    ///     sit exactly at a page boundary could differ from it by a hairline of floating-point noise.
    ///  3. <c>ResolveNamedString</c> used to compare each candidate's raw Y against a symmetric
    ///     <c>[pageY - epsilon, pageY + pageHeight + epsilon)</c> window computed independently per page.
    ///     Two boxes at the very top of different columns on the *same* page can land on the identical Y
    ///     (both are "row 1" of their own column) - when that shared Y sits within a hairline of a page
    ///     boundary, the symmetric epsilon widened at both ends of *every* page's window let a box on the
    ///     far side of the boundary satisfy the near side's window too, so a paragraph opening column 2 of
    ///     one page could be picked as the running header's "last" value for the *previous* page. Fixed by
    ///     attributing every candidate to a single, unambiguous pagination slot up front (the same one a
    ///     fresh top-of-page box would resolve to via <c>SlotStartingAt</c> elsewhere) rather than testing
    ///     window membership per page.
    /// </summary>
    public class MarginBoxRendererNamedStringTests
    {
        // Mirrors HtmlContainerInt.SlotStartingAt's non-variable-geometry fallback (MarginTop = 0): a
        // value within PageBoundaryEpsilon *above* a page boundary is nudged onto the later page, exactly
        // as a genuinely fresh top-of-page box would be.
        private static int PageIndexOf(double y, double pageHeight) =>
            (int)((y + HtmlContainerInt.PageBoundaryEpsilon) / pageHeight);

        private static string Resolve(string name, string keyword, double pageY, double pageHeight, IReadOnlyList<NamedString> namedStrings) =>
            MarginBoxRenderer.ResolveNamedString(name, keyword, PageIndexOf(pageY, pageHeight), y => PageIndexOf(y, pageHeight), namedStrings);

        [Fact]
        public void First_ValueExactlyAtPageStart_Resolves()
        {
            var namedStrings = new List<NamedString>
            {
                new("term", "before", 90),
                new("term", "first-on-page", 100),
                new("term", "second-on-page", 150),
            };

            var result = Resolve("term", "first", pageY: 100, pageHeight: 100, namedStrings);

            Assert.Equal("first-on-page", result);
        }

        [Fact]
        public void First_ValueHairlineBelowPageStart_StillResolvesAsFirstOnPage()
        {
            // Simulates the real bug: a value meant to sit exactly at the page boundary (100) but computed
            // via a slightly different accumulation path landing at 99.999999991 - without tolerance this
            // value is wrongly excluded from the page, falling through to the next entry.
            var namedStrings = new List<NamedString>
            {
                new("term", "before", 90),
                new("term", "first-on-page", 99.999999991),
                new("term", "second-on-page", 150),
            };

            var result = Resolve("term", "first", pageY: 100, pageHeight: 100, namedStrings);

            Assert.Equal("first-on-page", result);
        }

        [Fact]
        public void First_ValueGenuinelyOnPreviousPage_DoesNotResolveAsFirstOnPage()
        {
            // A value meaningfully earlier than the page boundary (not just floating-point noise) must
            // still be correctly excluded - the epsilon must not swallow genuinely different-page content.
            var namedStrings = new List<NamedString>
            {
                new("term", "previous-page", 60),
                new("term", "second-on-page", 150),
            };

            var result = Resolve("term", "first", pageY: 100, pageHeight: 100, namedStrings);

            Assert.Equal("second-on-page", result);
        }

        [Fact]
        public void Last_ValueHairlineAtPageEnd_ResolvesToTheNextPageInstead()
        {
            // Superseded expectation: a value this close to a page's *end* boundary is, by the same
            // top-edge convention SlotStartingAt already applies to a fresh box opening a page, attributed
            // to the page it borders rather than the one it trails - a box whose own top sits this close
            // to a boundary is, in practice, either freshly placed at the next page's top by a break or so
            // short as to be indistinguishable from one, never a real box whose content is comfortably
            // contained in the page ending there. Treating it as "genuinely this page's last value" would
            // reopen exactly the ambiguity #3 above describes: the same Y both closing one page's window
            // and opening the next's. Confirmed correct rather than merely different: the next test shows
            // the same value now correctly resolves as the *next* page's own "first".
            var namedStrings = new List<NamedString>
            {
                new("term", "first-on-page", 110),
                new("term", "at-the-boundary", 199.999999991),
                new("term", "next-page", 210),
            };

            var result = Resolve("term", "last", pageY: 100, pageHeight: 100, namedStrings);

            Assert.Equal("first-on-page", result);
        }

        [Fact]
        public void First_ValueHairlineAtPageEnd_ResolvesAsFirstOnTheFollowingPage()
        {
            // The companion to the test above, on the page "at-the-boundary" (199.999999991) actually
            // belongs to.
            var namedStrings = new List<NamedString>
            {
                new("term", "first-on-page", 110),
                new("term", "at-the-boundary", 199.999999991),
                new("term", "next-page", 210),
            };

            var result = Resolve("term", "first", pageY: 200, pageHeight: 100, namedStrings);

            Assert.Equal("at-the-boundary", result);
        }

        [Fact]
        public void Last_ColumnTopsOfTheNextPageDoNotLeakOntoThisPagesLast()
        {
            // The actual shape of the css4.pub Icelandic dictionary bug: two different paragraphs, each
            // the first content of its own column on the *next* page, land on the identical Y - both are
            // "row 1" of their own column, here 200, the next page's own start (and so this page's exact
            // end boundary). Neither may resolve as *this* page's "last", regardless of list order, even
            // though both sit exactly on this page's (epsilon-widened, under the old symmetric-window
            // design) far boundary.
            var namedStrings = new List<NamedString>
            {
                new("term", "last-on-this-page", 150),
                new("term", "column-1-top-of-next-page", 200),
                new("term", "column-2-top-of-next-page", 200),
            };

            var result = Resolve("term", "last", pageY: 100, pageHeight: 100, namedStrings);

            Assert.Equal("last-on-this-page", result);
        }
    }
}
