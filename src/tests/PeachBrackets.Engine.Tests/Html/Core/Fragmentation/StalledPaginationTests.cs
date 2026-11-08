using PeachBrackets.Html;
using PeachBrackets.Html.Dom;
using PeachBrackets.Html.Fragmentation;
using PeachPDF.Tests.TestSupport;
using System.Threading.Tasks;

namespace PeachBrackets.Engine.Tests.Html.Core.Fragmentation
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

            // This document stalled because an atomic inline took no place in the walk's order, so a break before one resumed at
            // the first of a run and every pass placed them again; with each given its own ordinal it paginates without help. The
            // last-resort relayout is still the way out of a stall that has some other cause, so either way is accepted here, and
            // the comparison of tokens it relies on is tested directly below.
            Assert.True(container.LastResortRelayouts <= 1, $"{container.LastResortRelayouts} last-resort relayouts");

            // No word of the document is lost, whichever way it finished.
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

        // A table cell that spans rows past the last one, in a table with a repeating header (its height pinned, so the float is a few points too tall for the room under it whatever the font), holding a float that fits no band: no pass
        // places it, and every pass resumes at the same point. The table token carries the row-span bookkeeping, and a cell placed again on
        // each pass was listed once more every time, so the token was never equal to the one before and the stall went undetected for the
        // whole pass cap (a hang, as far as the caller could tell: a document of this shape ran for minutes).
        [Theory]
        [InlineData("<td rowspan='4'><table><tbody><tr><td rowspan='3'>")]
        [InlineData("<td rowspan='4'><table><tbody><tr><td>")]
        [InlineData("<td><table><tbody><tr><td rowspan='3'>")]
        public async Task ACellSpanningRowsThatHoldsAFloatNoBandCanHold_IsRecoveredFromWithinAFewPasses(string openCells)
        {
            var html = "<style>body { margin: 0; font: 12pt Pinned } th { padding: 0; line-height: 20pt } table { border-spacing: 0 }</style><table><thead><tr><th>w1</th></tr></thead><tbody><tr>" +
                       openCells + "<div style='float:right;width:60%;margin:0 0 6pt 0;height:86pt'></div>w2</td></tr></tbody></table></td></tr></tbody></table>";

            var (root, container) = await LayoutHarness.LayoutAsync(html, pageWidth: 300, pageHeight: 160, margin: 20,
                configureRenderContext: renderContext => BundledFonts.RegisterFont(renderContext, BundledFonts.Ttf, "Pinned"),
                configureContainer: c => c.MaxFragmentainersOverride = 300);

            Assert.True(container.FragmentainerPasses < 40, $"{container.FragmentainerPasses} passes");
            Assert.Equal(0, container.LastResortRelayouts);
            Assert.Contains("w2", string.Join(" ", AllText(root)));
        }

        [Fact]
        public void FlexGridAndFlexColumnTokensAreComparedWithoutTheirSlot_AndByTheirStuckItems()
        {
            var container = new CssBox(null, null);
            var item = new CssBox(null, null);
            var body = new CssBox(null, null);
            IReadOnlyList<IReadOnlyList<CssBox>> lines = [[item]];

            BreakToken Item(int slot, int word) => new InlineBreakToken(body, slot, [0], word, 1);
            BreakToken Flex(int slot, int line, int word) => new FlexBreakToken(container, slot, line, lines,
                [new UnfinishedFlexItem(item, Item(slot, word))], [], default);
            BreakToken Grid(int slot, int row, int word) => new GridBreakToken(container, slot, row, lines,
                [new UnfinishedGridItem(item, Item(slot, word))], [], new Dictionary<CssBox, GridSubgridContext>(), default);
            BreakToken Column(int slot, int index, int word) => new FlexColumnBreakToken(container, slot, lines,
                [new ColumnLineCursor(0, index, Item(slot, word))], [], default);

            Assert.True(HtmlContainerInt.IsSameProgress(Flex(5, 0, 2), Flex(4, 0, 2)));
            Assert.False(HtmlContainerInt.IsSameProgress(Flex(5, 1, 2), Flex(4, 0, 2)));
            Assert.False(HtmlContainerInt.IsSameProgress(Flex(5, 0, 3), Flex(4, 0, 2)));

            Assert.True(HtmlContainerInt.IsSameProgress(Grid(5, 0, 2), Grid(4, 0, 2)));
            Assert.False(HtmlContainerInt.IsSameProgress(Grid(5, 1, 2), Grid(4, 0, 2)));

            Assert.True(HtmlContainerInt.IsSameProgress(Column(5, 0, 2), Column(4, 0, 2)));
            Assert.False(HtmlContainerInt.IsSameProgress(Column(5, 1, 2), Column(4, 0, 2)));
        }

        private const string FloatUnderHeader =
            "<style>body { margin: 0; font: 12pt Pinned } th { padding: 0; line-height: 20pt } table { border-spacing: 0 }</style>" +
            "<table><thead><tr><th>w1</th></tr></thead><tbody><tr><td>" +
            "<div style='float:right;width:60%;margin:0 0 6pt 0;height:86pt'></div>w2</td></tr></tbody></table>";

        // A table with a repeated header and a float no band can hold, inside a flex or grid container (whose break tokens the
        // driver could not compare) or an absolutely positioned box (which has a pass loop of its own): it finishes.
        [Theory]
        [InlineData("<div style='display:flex'>", "</div>")]
        [InlineData("<div style='display:grid'>", "</div>")]
        [InlineData("<div style='position:absolute;left:0;top:0;width:250pt'>", "</div>")]
        public async Task AStalledTableInsideFlexGridOrAnAbsoluteBox_IsRecoveredFromWithinAFewPasses(string open, string close)
        {
            var (root, container) = await LayoutHarness.LayoutAsync(open + FloatUnderHeader + close, pageWidth: 300, pageHeight: 160, margin: 20,
                configureRenderContext: renderContext => BundledFonts.RegisterFont(renderContext, BundledFonts.Ttf, "Pinned"),
                configureContainer: c => c.MaxFragmentainersOverride = 300);

            Assert.True(container.FragmentainerPasses < 60, $"{container.FragmentainerPasses} passes");
            Assert.Contains("w2", string.Join(" ", AllText(root)));
        }

        // A float under a repeated header that fits no band (the line beside it is moved below it, where a page that also holds the header
        // has no room): the line stays beside the float instead of walking page after page, so no page holds only the header.
        [Theory]
        [InlineData(80)]
        [InlineData(86)]
        [InlineData(90)]
        [InlineData(95)]
        [InlineData(100)]
        public async Task AFloatThatFitsNoBandUnderARepeatedHeader_DoesNotMakeHeaderOnlyPages(int floatHeight)
        {
            var html = "<style>body { margin: 0; font: 12pt Pinned } th { padding: 0; line-height: 20pt } table { border-spacing: 0 }</style><table><thead><tr><th>w1</th></tr></thead><tbody><tr><td><table><tbody><tr><td>" +
                       $"<div style='float:right;width:60%;margin:0 0 6pt 0;height:{floatHeight}pt'></div>w2</td></tr></tbody></table></td></tr></tbody></table>";
            var (root, container) = await LayoutHarness.LayoutAsync(html, pageWidth: 300, pageHeight: 160, margin: 20,
                configureRenderContext: renderContext => BundledFonts.RegisterFont(renderContext, BundledFonts.Ttf, "Pinned"),
                configureContainer: c => c.MaxFragmentainersOverride = 300);

            Assert.True(container.FragmentainerPasses <= 3, $"{container.FragmentainerPasses} passes");
            Assert.Equal(0, container.LastResortRelayouts);
            Assert.Contains("w2", string.Join(" ", AllText(root)));
        }

        [Fact]
        public async Task AThreeLevelNestedTableWithARepeatedHeaderInTwoColumns_Finishes()
        {
            var html = "<style>body{margin:0;font:10pt/12pt sans-serif} td,th{border:1px solid #000;padding:2pt;vertical-align:top} table{border-collapse:separate}</style><div style='columns:2'><table><thead><tr><th></th></tr></thead><tbody><tr><td><table><tbody><tr><td><table><tbody><tr><td><table><tbody><tr><td></td></tr><tr><td><div style='height:20pt'></div></td></tr></tbody></table></td></tr><tr><td> w11179 w11180 w11181 w11182 w11183 w11184 w11185 w11186 w11187 w11188 w11189 w11190 w11191 </td></tr></tbody></table></td></tr></tbody></table></td></tr></tbody></table></div>";
            var (root, container) = await LayoutHarness.LayoutAsync(html, pageWidth: 300, pageHeight: 160, margin: 20,
                configureRenderContext: renderContext => BundledFonts.RegisterFont(renderContext, BundledFonts.Ttf, "Pinned"),
                configureContainer: c => c.MaxFragmentainersOverride = 300);

            Assert.True(container.FragmentainerPasses < 60, $"{container.FragmentainerPasses} passes");
            Assert.Contains("w11191", string.Join(" ", AllText(root)));
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
