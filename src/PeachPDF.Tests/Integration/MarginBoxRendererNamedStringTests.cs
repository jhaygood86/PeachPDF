using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Entities;
using System.Collections.Generic;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Direct unit tests for <see cref="MarginBoxRenderer.ResolveNamedString"/>'s page-boundary matching -
    /// exercised directly (not through full HTML->PDF generation) since it's a pure function over an
    /// explicit <see cref="NamedString"/> list and page Y-range, per this repo's testing conventions.
    ///
    /// Regression coverage for two compounding bugs found while investigating wrong running-header
    /// pairing on real content pages of css4.pub's Icelandic dictionary:
    ///  1. A page's true content range is [pageY + MarginTop, pageY + MarginTop + pageHeight), not
    ///     [pageY, pageY + pageHeight), since content's own coordinate system starts at MarginTop, not 0
    ///     (see PdfGenerator.AddPdfPages, which now adds +container.MarginTop when computing pageY).
    ///  2. A NamedString's Y and the page boundary are computed via independent accumulation paths (row/
    ///     column layout math vs. per-page scroll-offset accumulation), so a value genuinely meant to sit
    ///     exactly at a page boundary can differ from it by a hairline of floating-point noise -
    ///     PageBoundaryEpsilon absorbs that without misattributing genuinely different-page content.
    /// </summary>
    public class MarginBoxRendererNamedStringTests
    {
        [Fact]
        public void First_ValueExactlyAtPageStart_Resolves()
        {
            var namedStrings = new List<NamedString>
            {
                new("term", "before", 90),
                new("term", "first-on-page", 100),
                new("term", "second-on-page", 150),
            };

            var result = MarginBoxRenderer.ResolveNamedString("term", "first", pageY: 100, pageHeight: 100, namedStrings);

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

            var result = MarginBoxRenderer.ResolveNamedString("term", "first", pageY: 100, pageHeight: 100, namedStrings);

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

            var result = MarginBoxRenderer.ResolveNamedString("term", "first", pageY: 100, pageHeight: 100, namedStrings);

            Assert.Equal("second-on-page", result);
        }

        [Fact]
        public void Last_ValueHairlineBelowPageEnd_StillResolvesAsLastOnPage()
        {
            var namedStrings = new List<NamedString>
            {
                new("term", "first-on-page", 110),
                new("term", "last-on-page", 199.999999991),
                new("term", "next-page", 210),
            };

            var result = MarginBoxRenderer.ResolveNamedString("term", "last", pageY: 100, pageHeight: 100, namedStrings);

            Assert.Equal("last-on-page", result);
        }
    }
}
