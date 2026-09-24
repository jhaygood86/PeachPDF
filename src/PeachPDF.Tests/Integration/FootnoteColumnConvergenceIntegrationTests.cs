using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;
using static PeachPDF.Tests.TestSupport.LayoutHarness;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Column-scoped footnote areas add a feedback edge to the footnote convergence loop: reserving room at the
    /// foot of column 1 can push the paragraph carrying the call into column 2, which moves the reservation
    /// and lengthens column 1 again (issue #1270).
    /// </summary>
    public class FootnoteColumnConvergenceIntegrationTests
    {
        private static string Document(int columnHeightPt, int noteWords, string reference = "column", int callEvery = 3)
        {
            var lines = new StringBuilder();

            for (var i = 1; i <= 24; i++)
            {
                // A call on every few lines, so whichever line a reservation pushes across the boundary
                // carries one.
                var call = i % callEvery == 0
                    ? $"<sup style='float:footnote; float-reference:{reference}'>{string.Join(" ", Enumerable.Repeat("note", noteWords))}</sup>"
                    : "";
                lines.Append($"<div>Line {i}{call}</div>");
            }

            return "<style>div,sup{font:10pt/10pt sans-serif}</style>" +
                   $"<div style='columns:2; column-gap:10pt; column-fill:auto; height:{columnHeightPt}pt; width:300pt'>{lines}</div>";
        }

        [Fact]
        public async Task DenseColumnScopedNotes_SettleInsteadOfEndingOnTheCap()
        {
            // A call on every third line of a fixed-height two-column container: reserving room at the foot of
            // a column pushes lines across the column break, which moves the reservations, and so on. Before
            // the guard every one of these layouts stopped on the six-pass cap (the loop had not settled) - the
            // page-scoped equivalent always settled.
            var unsettled = new StringBuilder();
            var swept = 0;

            for (var height = 60; height <= 200; height += 10)
            {
                foreach (var noteWords in new[] { 1, 6 })
                {
                    var (_, container) = await LayoutAsync(Document(height, noteWords), pageWidth: 400, pageHeight: 400, margin: 20);
                    swept++;

                    if (!container.FootnoteLoopSettled)
                    {
                        unsettled.AppendLine($"height {height}pt, {noteWords} note word(s): {container.FootnoteResolvePasses} passes");
                    }

                    // Every call's note reaches a note area on a page, whichever state the loop stopped in.
                    var bodies = container.FragmentTree!.Fragmentainers.Sum(f => f.FootnoteAreas?.Sum(a => a.Bodies.Count) ?? 0);
                    Assert.Equal(container.FootnoteCalls.Count, bodies);
                }
            }

            Assert.True(unsettled.Length == 0, $"{swept} layouts swept; the footnote loop hit its cap in:\n{unsettled}");
        }

        [Fact]
        public async Task PageScopedNotes_StillSettleAsBefore()
        {
            var (_, container) = await LayoutAsync(Document(120, 3, reference: "page"), pageWidth: 400, pageHeight: 400, margin: 20);

            Assert.True(container.FootnoteLoopSettled);
        }

        [Fact]
        public async Task SparseColumnScopedNotes_SettleInFewPassesWithoutAnyFloor()
        {
            // A document that settles on its own never repeats a state, so nothing is held.
            var (_, container) = await LayoutAsync(Document(120, 3, callEvery: 8), pageWidth: 400, pageHeight: 400, margin: 20);

            Assert.True(container.FootnoteLoopSettled);
            Assert.True(container.FootnoteResolvePasses < 6, $"took {container.FootnoteResolvePasses} passes");
        }
    }
}
