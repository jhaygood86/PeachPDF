using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Fragmentation;
using PeachPDF.Tests.TestSupport;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Html.Core.Fragmentation
{
    /// <summary>
    /// A pass that resumes where the one before did, one fragmentainer later, has placed nothing. The driver used to notice only
    /// an exact repeat of a token, and a token names the slot it resumes in, so such a run was never seen and went on for the
    /// whole 100,000-pass cap (minutes, for a few hundred bytes of HTML).
    /// </summary>
    public class StalledPaginationTests
    {
        private const string StallingDocument =
            "<style>body { font: 12pt Pinned } .ib { display: inline-block; width: 40pt }</style>" +
            "<span class='ib'><blockquote> \u00A0 beta alpha rho alpha\tkappa\nrho theta gamma xi theta delta \u00A0 pi\nmu theta sigma tau delta mu</blockquote>" +
            "pi gamma kappa pialpha kappa \u00A0 epsilon beta kappa omicron omicron\u00A0theta beta epsilon theta mu\u00A0</a>\n" +
            "kappa omicron theta epsilon rho\tdelta lambda epsilon gamma xi theta\nmu\txi mu sigmaxi alpha alpha\nomicron iota sigma sigma epsilon\n</span>  <br>";

        [Fact]
        public async Task ARunThatStopsMakingProgress_IsRecoveredFromWithinAFewPasses()
        {
            // The stall depends on how the words measure against the 40pt box, so the font is a bundled one rather than whatever the host resolves (the first version of this test stalled on Windows and not on Linux or macOS).
            var (root, container) = await LayoutHarness.LayoutAsync(StallingDocument, configureRenderContext: renderContext => BundledFonts.RegisterFont(renderContext, BundledFonts.Ttf, "Pinned"));

            Assert.True(container.FragmentainerPasses < 100, $"{container.FragmentainerPasses} passes");
            Assert.Equal(1, container.LastResortRelayouts);

            // The recovery lays the remainder out whole, so no word of the document is lost.
            var text = string.Join(" ", AllText(root));
            Assert.Contains("beta", text);
            Assert.Contains("epsilon", text);
        }

        [Fact]
        public void TwoTokensThatDifferOnlyInTheSlotTheyResumeIn_AreTheSameProgress()
        {
            var body = new CssBox(null, null);
            var html = new CssBox(null, null);

            var first = Chain(html, body, slot: 3, word: 0);
            var second = Chain(html, body, slot: 4, word: 0);

            Assert.NotEqual(first, second);
            Assert.True(HtmlContainerInt.IsSameProgress(second, first));
        }

        [Fact]
        public void TokensAtDifferentPlacesInTheContent_AreNotTheSameProgress()
        {
            var body = new CssBox(null, null);
            var html = new CssBox(null, null);

            Assert.False(HtmlContainerInt.IsSameProgress(Chain(html, body, 3, word: 0), Chain(html, body, 4, word: 5)));
            Assert.False(HtmlContainerInt.IsSameProgress(Chain(html, body, 3, word: 0), Chain(html, new CssBox(null, null), 4, word: 0)));
        }

        [Fact]
        public void ABlockTokenWithoutAChild_IsComparedOnItsOwnFields()
        {
            var box = new CssBox(null, null);

            Assert.True(HtmlContainerInt.IsSameProgress(
                new BlockBreakToken(box, 2, 1, null, true, null), new BlockBreakToken(box, 1, 1, null, true, null)));
            Assert.False(HtmlContainerInt.IsSameProgress(
                new BlockBreakToken(box, 2, 1, null, true, null), new BlockBreakToken(box, 1, 2, null, true, null)));
        }

        [Fact]
        public void ATableTokenIsComparedWithoutItsSlot_AndByItsRowAndCells()
        {
            var table = new CssBox(null, null);
            var cell = new CssBox(null, null);
            var body = new CssBox(null, null);

            BreakToken Table(int slot, int row, int word) => new TableBreakToken(
                table, slot, row, 579,
                [new UnfinishedTableCell(0, cell, new InlineBreakToken(body, slot, [0], word, 1))],
                [], new Dictionary<int, IReadOnlyList<CssBox>>());

            Assert.True(HtmlContainerInt.IsSameProgress(Table(5, 0, 2), Table(4, 0, 2)));
            Assert.False(HtmlContainerInt.IsSameProgress(Table(5, 1, 2), Table(4, 0, 2)));
            Assert.False(HtmlContainerInt.IsSameProgress(Table(5, 0, 3), Table(4, 0, 2)));
        }

        private static BreakToken Chain(CssBox html, CssBox body, int slot, int word) =>
            new BlockBreakToken(html, slot, 1,
                new InlineBreakToken(body, slot, [0], word, 1, 0, true), false, null);

        private static IEnumerable<string> AllText(CssBox box)
        {
            if (box.Text is { } text) yield return text;

            foreach (var child in box.Boxes)
                foreach (var t in AllText(child))
                    yield return t;
        }
    }
}
