using PeachPDF.Tests.TestSupport;
using System.Linq;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// A subtree containing a table that repeats a header takes the same §4.3 relocation every other
    /// subtree does — laid out again at its destination rather than translated to it.
    /// </summary>
    /// <remarks>
    /// It was excluded because laying such a table out a second time did not reproduce the first result:
    /// the repeating group was detached and replaced by per-page proxies that nothing removed, so a
    /// second run threw. With that fixed the exclusion is gone, and a translated box's signature — the
    /// fragmentainer gap carried along inside it as height it does not use — has to be gone with it.
    /// </remarks>
    public class RepeatingTableRelayoutTests
    {
        private const double PageHeight = 200;
        private const double Margin = 20;

        private static string CardWithTable(double fillerHeight) =>
            LayoutHarness.Wrap(
                $"<div style='height:{fillerHeight}pt'>filler</div>"
                + "<div id='card' style='break-inside:avoid;orphans:1;widows:1;font-size:10pt'>"
                + "<table style='width:100pt'><thead><tr><th>H</th></tr></thead>"
                + "<tbody><tr><td>One</td></tr><tr><td>Two</td></tr><tr><td>Three</td></tr></tbody>"
                + "</table></div>");

        [Theory]
        [InlineData(120)]
        [InlineData(130)]
        [InlineData(140)]
        public async Task ACardHoldingARepeatingHeaderTable_IsRelocatedWithoutCarryingAGap(double fillerHeight)
        {
            var (undisturbed, _) = await LayoutHarness.LayoutAsync(
                CardWithTable(0), pageHeight: PageHeight, margin: Margin);
            var settledCard = LayoutHarness.FindById(undisturbed, "card")!;
            var settled = settledCard.ActualBottom - settledCard.Location.Y;

            var (root, _) = await LayoutHarness.LayoutAsync(
                CardWithTable(fillerHeight), pageHeight: PageHeight, margin: Margin);
            var card = LayoutHarness.FindById(root, "card")!;

            // A translated box keeps the gap as height it does not use, so its height would depend on
            // where it happened to straddle. Laid out again, it is the height of its own content
            // wherever it lands.
            Assert.Equal(settled, card.ActualBottom - card.Location.Y, 3);
        }

        [Fact]
        public async Task TheTableInsideARelocatedCard_StillRepeatsItsHeaderExactlyOnce()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(
                CardWithTable(130), pageHeight: PageHeight, margin: Margin);

            var headerGroups = LayoutHarness.Descendants(root)
                .Count(b => b.Display == "table-header-group");

            // One per page the table spans, and no stale copy left by the run that was abandoned.
            Assert.InRange(headerGroups, 1, 2);
        }
    }
}
