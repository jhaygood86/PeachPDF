using PeachBrackets.Html;
using PeachBrackets.Html.Dom;
using PeachPDF.Tests.TestSupport;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace PeachBrackets.Engine.Tests.Integration
{
    /// <summary>
    /// Laying a flex or grid container out again reproduces the geometry of every box in it, anonymous ones included. A flex or grid item's
    /// translation used to add itself to the <c>Location</c> of a plain inline box on every pass, which paint never reads, so only a test
    /// that snapshots all boxes can see it.
    /// </summary>
    public class FlexGridRelayoutAnonymousBoxTests
    {
        private static string AllBoxes(CssBox root, HtmlContainerInt container) =>
            string.Join("|", LayoutHarness.Descendants(root).Select(b => string.Format(
                CultureInfo.InvariantCulture, "{0}:{1}@({2:F3},{3:F3})-({4:F3},{5:F3})",
                b.HtmlTag?.Name ?? "anon", b.HtmlTag?.TryGetAttribute("id"), b.Location.X, b.Location.Y, b.ActualRight, b.ActualBottom)));

        private static async Task AssertStable(string body, double pageHeight = 842)
        {
            var snapshots = await LayoutHarness.LayoutRepeatedlyAsync(LayoutHarness.Wrap(body), passes: 4, AllBoxes, pageHeight: pageHeight);

            Assert.Equal(snapshots[1], snapshots[2]);
            Assert.Equal(snapshots[2], snapshots[3]);
        }

        private const string Inline = "<span>one</span> two <b>three</b> <i>four</i> five six seven eight nine ten eleven twelve";

        [Theory]
        [InlineData("display:flex;height:200pt;align-items:center")]
        [InlineData("display:flex;height:200pt;align-items:flex-end")]
        [InlineData("display:flex;height:200pt;flex-wrap:wrap;align-content:center")]
        [InlineData("display:flex;flex-direction:column;height:300pt;justify-content:center")]
        [InlineData("display:flex;height:200pt;align-items:baseline")]
        public async Task AFlexContainerAligningItsItems_LaidOutAgain_ReproducesEveryBox(string style)
        {
            await AssertStable($"<div id='c' style='{style};width:300pt'>"
                + $"<div id='a' style='width:100pt'>{Inline}</div><div id='b' style='width:100pt'>x {Inline}</div></div>");
        }

        [Theory]
        [InlineData("display:grid;height:200pt;grid-template-columns:1fr 1fr;align-items:center")]
        [InlineData("display:grid;height:200pt;grid-template-columns:1fr 1fr;align-items:end")]
        [InlineData("display:grid;height:300pt;grid-template-rows:100pt 100pt;align-content:center")]
        [InlineData("display:grid;height:200pt;grid-template-columns:1fr 1fr;align-items:baseline")]
        public async Task AGridContainerAligningItsItems_LaidOutAgain_ReproducesEveryBox(string style)
        {
            await AssertStable($"<div id='c' style='{style};width:300pt'>"
                + $"<div id='a'>{Inline}</div><div id='b'>x {Inline}</div></div>");
        }

        [Fact]
        public async Task AFlexItemTranslatedAcrossAPageBreak_LaidOutAgain_ReproducesEveryBox()
        {
            await AssertStable("<div style='height:260pt'></div><div id='c' style='display:flex;width:300pt'>"
                + $"<div id='a'>{Inline}<br>{Inline}<br>{Inline}</div><div id='b'>{Inline}</div></div>", pageHeight: 300);
        }
        // The other shapes #1440 listed: a translation of an ancestor that is not a flex or grid item.
        [Fact]
        public async Task AVerticallyCenteredTableCell_LaidOutAgain_ReproducesEveryBox()
        {
            await AssertStable("<table style='width:300pt'><tr><td id='a' style='vertical-align:middle;height:200pt'>" + Inline
                + "</td><td id='b'>x</td></tr></table>");
        }

        [Fact]
        public async Task AVerticallyCenteredTableCellAcrossAPageBreak_LaidOutAgain_ReproducesEveryBox()
        {
            await AssertStable("<div style='height:240pt'></div><table style='width:300pt'><tr><td id='a' style='vertical-align:middle;height:200pt'>"
                + Inline + "</td><td id='b'>x</td></tr></table>", pageHeight: 300);
        }

        [Fact]
        public async Task ARepeatedTableHeaderWithInlineContent_LaidOutAgain_ReproducesEveryBox()
        {
            var rows = string.Concat(Enumerable.Range(1, 30).Select(i => $"<tr><td>Row {i} <b>bold</b> cell</td></tr>"));
            await AssertStable("<table style='width:300pt'><thead><tr><th><span>Head</span> <i>one</i></th></tr></thead><tbody>" + rows
                + "</tbody></table>", pageHeight: 300);
        }

        [Fact]
        public async Task AMultiColumnContainerWithInlineContent_LaidOutAgain_ReproducesEveryBox()
        {
            await AssertStable("<div id='c' style='column-count:2;width:300pt'>" + string.Concat(Enumerable.Repeat($"<p>{Inline}</p>", 12)) + "</div>",
                pageHeight: 300);
        }
    }
}
