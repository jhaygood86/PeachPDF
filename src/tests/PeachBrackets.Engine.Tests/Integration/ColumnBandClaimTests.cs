using PeachBrackets.Html.Dom;
using PeachBrackets.Html.Fragments;
using PeachPDF.Tests.TestSupport;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PeachBrackets.Engine.Tests.Integration
{
    /// <summary>
    /// A multi-column container whose text follows a block with a large negative margin-top places that text
    /// above the columns' band (#1896, #1897). Every word the document authored must still be claimed by a
    /// fragment, as it is when the same text sits in an explicit wrapper.
    /// </summary>
    public class ColumnBandClaimTests
    {
        private const string Narrow40 =
            "<div id='mc' style='width:40px;columns:3'>                    "
            + "<div style='float:left;width:20px;height:40px;'>FL1</div>                     w00077            "
            + "<div style='float:none;width:20px;height:20px;margin:-100px 0px 0px 0px;'>FL4</div>"
            + "                    w00110 w00111 w00112      </div>";

        private static string Negative60() =>
            "<div id='mc' style='width:500px;column-width:60px'>"
            + string.Join(" ", Enumerable.Range(1, 58).Select(i => $"w{i:D5}"))
            + " <div style='float:left;width:42px;height:89px;'>F</div> "
            + string.Join(" ", Enumerable.Range(59, 26).Select(i => $"w{i:D5}"))
            + " <div style='float:right;width:33px;height:51px;'>F</div> "
            + string.Join(" ", Enumerable.Range(85, 20).Select(i => $"w{i:D5}"))
            + " <div style='float:none;width:20px;height:20px;margin:-20px 0 0 0'>N</div> "
            + string.Join(" ", Enumerable.Range(105, 10).Select(i => $"w{i:D5}"))
            + " <div style='float:none;width:20px;height:20px;margin:-200px 0 0 0'>N</div> "
            + string.Join(" ", Enumerable.Range(115, 9).Select(i => $"w{i:D5}"))
            + "</div>";

        [Theory]
        [InlineData("narrow40")]
        [InlineData("negative60")]
        public async Task TextAfterANegativeMarginBlock_IsClaimedByAColumn(string shape)
        {
            var body = shape == "narrow40" ? Narrow40 : Negative60();
            var (root, container) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(body), pageHeight: 850, margin: 10);

            var authored = LayoutHarness.Descendants(root)
                .SelectMany(b => b.Words).Where(w => w.Text is { } t && t.StartsWith('w')).ToList();
            var claimed = container.FragmentTree!.Fragmentainers
                .SelectMany(f => Flatten(f.Root)).SelectMany(f => f.Words).Select(w => w.Word).ToHashSet();

            var lost = authored.Where(w => !claimed.Contains(w)).ToList();
            var mc = LayoutHarness.Descendants(root).First(b => b.EstablishesMultiColumnContext);

            Assert.True(lost.Count == 0,
                $"{lost.Count} of {authored.Count} words unclaimed; mc.Location.Y={mc.Location.Y}; "
                + string.Join(", ", lost.Take(10).Select(w => $"{w.Text}@({w.Left:F0},{w.Top:F0})")));
        }

        private static IEnumerable<BoxFragment> Flatten(BoxFragment fragment)
        {
            yield return fragment;

            foreach (var child in fragment.Children)
            {
                foreach (var descendant in Flatten(child))
                {
                    yield return descendant;
                }
            }
        }
    }
}
