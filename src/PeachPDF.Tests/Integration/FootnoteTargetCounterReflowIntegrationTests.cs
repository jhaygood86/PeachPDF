using PeachPDF.Html.Core;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;
using static PeachPDF.Tests.TestSupport.LayoutHarness;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// A footnote's page (its call, its reserved room, its note area) has to describe the layout that is
    /// finally emitted, even when resolving <c>target-counter(_, page)</c> changes pagination after the
    /// footnotes have settled: the page-number text can change line-breaking, which can move a footnote call
    /// across a page break (issue #757).
    /// </summary>
    public class FootnoteTargetCounterReflowIntegrationTests
    {
        /// <summary>
        /// A table-of-contents line whose page number is one digit in the first layout and two in the
        /// last, so it wraps to a second line once resolved, followed by a paragraph carrying a footnote and a
        /// long run of filler that puts the target on page 10 or later. Sweeping the measure and the spacer
        /// moves the footnote call across a page boundary as the toc line grows.
        /// </summary>
        private static string Document(int measurePt, int spacerPt) =>
            "<style>a::after{content:' ' target-counter(attr(href), page)} p{margin:0;font:10pt/20pt sans-serif}</style>" +
            $"<p style='width:{measurePt}pt'><a href='#end'>Chapter</a></p>" +
            $"<div style='height:{spacerPt}pt'></div>" +
            "<p>Text<sup style='float:footnote'>The note body</sup> after the call.</p>" +
            "<div style='height:1500pt'></div>" +
            "<p id='end'>The target</p>";

        [Fact]
        public async Task EveryFootnoteCall_IsOnThePageWhoseNoteAreaHoldsItsBody()
        {
            var failures = new StringBuilder();
            var swept = 0;

            for (var measure = 40; measure <= 100; measure += 6)
            {
                for (var spacer = 90; spacer <= 150; spacer += 10)
                {
                    var (_, container) = await LayoutAsync(Document(measure, spacer), pageWidth: 200, pageHeight: 200, margin: 20);
                    swept++;

                    var problem = Mismatch(container);
                    if (problem is not null) failures.AppendLine($"measure {measure}pt, spacer {spacer}pt: {problem}");
                }
            }

            Assert.True(failures.Length == 0, $"{swept} layouts swept; footnote areas disagreed with their calls in:\n{failures}");
        }

        /// <summary>
        /// Null when every page's note-area body count equals the number of calls on that page, else a
        /// description of the first page that disagrees.
        /// </summary>
        private static string? Mismatch(HtmlContainerInt container)
        {
            var callsByPage = new Dictionary<int, int>();

            foreach (var call in container.FootnoteCalls)
            {
                var top = call.Rectangles.Count > 0
                    ? call.Rectangles.Values.Min(r => r.Top)
                    : call.Words[0].Top;
                var page = container.PageIndexOf(top);
                callsByPage[page] = callsByPage.GetValueOrDefault(page) + 1;
            }

            var pages = container.FragmentTree!.Fragmentainers;

            for (var page = 0; page < pages.Count; page++)
            {
                var bodies = pages[page].FootnoteAreas?.Sum(a => a.Bodies.Count) ?? 0;
                var calls = callsByPage.GetValueOrDefault(page);

                if (bodies != calls) return $"page {page} holds {bodies} note bod(ies) for {calls} call(s)";
            }

            return null;
        }
    }
}
