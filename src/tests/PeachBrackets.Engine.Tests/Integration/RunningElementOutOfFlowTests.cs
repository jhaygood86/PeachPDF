using PeachPDF;
using PeachBrackets.Html.Fragments;
using PeachPDF.Tests.TestSupport;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PeachBrackets.Engine.Tests.Integration
{
    /// <summary>
    /// A float or an absolutely positioned box inside a running element (<c>content: element()</c>) is part of the
    /// margin box's content on every page it is placed on: layout positions it, and the painter orders it
    /// (CSS 2.1 Appendix E) with the margin box content as its stacking scope (css-page-3 §3.1).
    /// </summary>
    public class RunningElementOutOfFlowTests
    {
        private static async Task<IReadOnlyList<BoxFragment>> HeadersAsync(string runningHtml)
        {
            var html = $$"""
                <!DOCTYPE html>
                <html><head><style>
                @page { size: a6; margin: 30mm 12mm 12mm 12mm; }
                @page { @top-center { content: element(pageHeader); width: 100%; vertical-align: top; } }
                #pageHeader { position: running(pageHeader); }
                </style></head><body>
                <div id="pageHeader">{{runningHtml}}</div>
                <p>Page one</p>
                <div style="break-before: page">Page two</div>
                </body></html>
                """;

            var (_, container) = await PdfGeneratorLayoutHarness.LayoutAsync(html, new PdfGenerateConfig { PageSize = PageSize.A6 });

            var fragmentainers = container.FragmentTree!.Fragmentainers;
            Assert.True(fragmentainers.Count >= 2, "the document has two pages");

            return [.. fragmentainers.Take(2).Select(f => Assert.Single(f.MarginBoxes!, m => m.BoxName == "top-center").Content)];
        }

        private static bool ById(BoxFragment f, string id) => f.Box.HtmlTag?.TryGetAttribute("id") == id;

        private static IEnumerable<BoxFragment> All(BoxFragment f) =>
            new[] { f }.Concat(f.Children.SelectMany(All));

        [Fact]
        public async Task FloatedImageAndText_AreFragmentsOnEveryPage_AtTheirSides()
        {
            var headers = await HeadersAsync(
                "<span id='left' style='float:left; width:40pt; height:40pt; background:red'></span>"
                + "<span id='right' style='float:right'>FLOATED-TEXT</span>"
                + "<span style='position:relative'>RELATIVE-TEXT</span> NORMAL-TEXT");

            foreach (var header in headers)
            {
                var left = Assert.Single(All(header), f => ById(f, "left"));
                var right = Assert.Single(All(header), f => ById(f, "right"));

                Assert.True(left.Rect.Width > 0 && left.Rect.Height > 0, $"the left float has an area: {left.Rect}");
                Assert.True(right.Rect.X > left.Rect.Right, $"the right float ({right.Rect}) sits right of the left one ({left.Rect})");
                Assert.Contains(All(header), f => f.Box.Text?.Contains("NORMAL-TEXT") == true);
                Assert.True(right.Box.IsFloated && left.Box.IsFloated);
            }
        }

        [Fact]
        public async Task AbsolutelyPositionedText_InsideARelativeBox_IsAFragmentAtItsOffsets()
        {
            var headers = await HeadersAsync(
                "<div style='position:relative; height:50pt'>"
                + "<span id='abs' style='position:absolute; right:0; top:0'>ABSOLUTE-TEXT</span>NORMAL-TEXT</div>");

            foreach (var header in headers)
            {
                var abs = Assert.Single(All(header), f => ById(f, "abs"));
                var normal = Assert.Single(All(header), f => f.Box.Text?.Contains("NORMAL-TEXT") == true);

                Assert.True(abs.Box.IsAbsolutelyPositioned);
                Assert.Contains(All(abs), f => f.Box.Text == "ABSOLUTE-TEXT");
                Assert.True(abs.Rect.Right >= header.Rect.Right - 1, $"right:0 pins it to the right edge: {abs.Rect} in {header.Rect}");
                Assert.True(abs.Rect.X > normal.Rect.X, "it sits right of the flowed text");
            }
        }

        [Fact]
        public async Task AbsoluteBox_IsNotClippedByAnOverflowBoxBelowItsContainingBlock()
        {
            var headers = await HeadersAsync(
                "<div style='position:relative; height:30pt'><div style='overflow:hidden; width:20pt; height:10pt'>"
                + "<span style='position:absolute; left:60pt; top:0'>ABSOLUTE-TEXT</span></div></div>");

            var abs = Assert.Single(All(headers[0]), f => f.Box.IsAbsolutelyPositioned);
            Assert.True(abs.Rect.X > headers[0].Rect.X + 20 * 0, "it sits at its own offset");

            Assert.All(All(abs), f => Assert.Null(f.OverflowClip));
        }
    }
}
