using PeachPDF.Html.Core.Fragments;
using PeachPDF.Tests.TestSupport;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// A first child whose parent's top padding or border crosses the page foot
    /// (<see href="https://www.w3.org/TR/css-break-3/#break-margins">css-break-3 §5.2</see>).
    /// </summary>
    /// <remarks>
    /// The parent's content starts on the next page, but the child used to be placed on the page being
    /// filled, a margin below that content top, while its first line resumed at the next page's top: above
    /// the child's own box. The fixtures measure in the bundled Liberation Sans (Arial's metrics) on a
    /// 300×200pt page with 20pt margins, so page 1's band is [180, 340).
    /// </remarks>
    public class FirstChildAtThePageFootIntegrationTests
    {
        private const string Font = "FirstChildAtPageFootSans";
        private const double PageHeight = 200;
        private const double Margin = 20;

        [Theory]
        [InlineData("padding-top:5pt")]
        [InlineData("border-top:5pt solid #888")]
        public async Task ParentWhoseTopEdgeCrossesThePageFoot_MovesWhole_AndItsFirstChildsTextStartsInsideTheChild(string parentCss)
        {
            var words = string.Join(" ", Enumerable.Range(10, 40).Select(i => $"w1_{i}"));
            var html = "<!DOCTYPE html><html><head></head>" +
                       $"<body style='margin:0;font-family:\"{Font}\";font-size:10pt;line-height:12pt'>" +
                       "<div style='height:155.5pt'>w1_1</div>" +
                       $"<div id='parent' style='{parentCss}'><p id='p'>{words}</p></div>" +
                       "<p>w1_90 w1_91</p></body></html>";

            var (root, container) = await LayoutHarness.LayoutAsync(html, pageWidth: 300, pageHeight: PageHeight, margin: Margin,
                configureAdapter: adapter => BundledFonts.RegisterFont(adapter, BundledFonts.LiberationSans, Font));

            var parent = LayoutHarness.FindById(root, "parent")!;
            var p = LayoutHarness.FindById(root, "p")!;

            // The parent's top edge reaches past page 0's band, and it has nothing else there, so it moves.
            Assert.Equal(container.PageTopOf(1), parent.Location.Y, 3);

            // The paragraph's first line starts at the paragraph's own content top, not above it.
            Assert.Equal(p.ClientTop, p.LineBoxes[0].FlowTop!.Value, 3);

            var drawn = container.FragmentTree!.Fragmentainers
                .SelectMany(page => Flatten(page.Root).SelectMany(f => f.Words))
                .Select(w => w.Word.Text)
                .ToList();
            Assert.Equal(
                Enumerable.Range(10, 40).Select(i => $"w1_{i}").Order(),
                drawn.Where(w => w is not null && w.StartsWith("w1_") && int.Parse(w[3..]) is >= 10 and < 50).Order());
        }

        // The control: a parent whose content top is still on page 0 keeps its child there, with the child's
        // margin truncated only if the margin itself crosses the foot, as before.
        [Fact]
        public async Task ParentWhoseTopEdgeFitsOnThePage_StaysWhereItStarts()
        {
            var html = "<!DOCTYPE html><html><head></head>" +
                       $"<body style='margin:0;font-family:\"{Font}\";font-size:10pt;line-height:12pt'>" +
                       "<div style='height:140pt'>w1_1</div>" +
                       "<div id='parent' style='padding-top:5pt'><p id='p' style='margin:0'>w1_10 w1_11</p></div></body></html>";

            var (root, container) = await LayoutHarness.LayoutAsync(html, pageWidth: 300, pageHeight: PageHeight, margin: Margin,
                configureAdapter: adapter => BundledFonts.RegisterFont(adapter, BundledFonts.LiberationSans, Font));

            var parent = LayoutHarness.FindById(root, "parent")!;
            var p = LayoutHarness.FindById(root, "p")!;

            Assert.Equal(Margin + 140, parent.Location.Y, 3);
            Assert.Equal(parent.Location.Y + 5, p.Location.Y, 3);
            Assert.Equal(p.ClientTop, p.LineBoxes[0].FlowTop!.Value, 3);
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
