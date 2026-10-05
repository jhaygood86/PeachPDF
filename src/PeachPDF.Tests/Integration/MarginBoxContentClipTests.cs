using PeachPDF.Html.Core.Fragments;
using PeachPDF.PdfSharpCore;
using PeachPDF.Tests.TestSupport;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// A running element's fragments (<c>content: element()</c>) are built by the same
    /// <c>MarginBoxContentFragmentBuilder</c> as a footnote body's. Each fragment carries the clip of its nearest
    /// <c>overflow</c>-clipping ancestor, so a descendant of an <c>overflow: hidden</c> box inside the running
    /// element is clipped to it - as the same markup in the page is.
    /// </summary>
    public class MarginBoxContentClipTests
    {
        private static async Task<BoxFragment> RunningContentAsync(string runningHtml)
        {
            var html = $$"""
                <!DOCTYPE html>
                <html><head><style>
                @page { size: a6; margin: 12mm; }
                @page { @top-center { content: element(heading); font-size: 8pt; } }
                .running { position: running(heading); margin: 0; }
                </style></head><body>
                {{runningHtml}}
                <p>Body text.</p>
                </body></html>
                """;

            var (_, container) = await PdfGeneratorLayoutHarness.LayoutAsync(html, new PdfGenerateConfig { PageSize = PageSize.A6 });

            return Assert.Single(container.FragmentTree!.Fragmentainers[0].MarginBoxes, m => m.BoxName == "top-center").Content;
        }

        private static IEnumerable<BoxFragment> All(BoxFragment f) =>
            new[] { f }.Concat(f.Children.SelectMany(All));

        [Fact]
        public async Task DescendantOfAnOverflowHiddenBox_CarriesThatBoxsPaddingEdge()
        {
            var root = await RunningContentAsync(
                "<div class='running'><div id='clipper' style='overflow:hidden; width:40pt; border:2pt solid black; white-space:nowrap'>"
                + "a long line of running header text</div></div>");

            var clipper = All(root).First(f => f.Box.Overflow.Value == PeachPDF.CSS.Overflow.Hidden);
            var inside = All(clipper).Where(f => !ReferenceEquals(f, clipper) && f.Words.Count > 0).ToList();

            Assert.NotEmpty(inside);
            Assert.All(inside, f =>
            {
                Assert.NotNull(f.OverflowClip);
                Assert.Equal(clipper.Rect.Left + 2, f.OverflowClip!.Value.Left, 1);
                Assert.Equal(clipper.Rect.Right - 2, f.OverflowClip!.Value.Right, 1);
            });
        }

        [Fact]
        public async Task TheRunningElementItself_IsNotClippedByAnything()
        {
            // An overflow: hidden running element clips its content, not itself.
            var root = await RunningContentAsync(
                "<div class='running' style='overflow:hidden; width:40pt'>a long line of running header text</div>");

            Assert.Null(root.OverflowClip);
            Assert.All(All(root).Skip(1), f => Assert.NotNull(f.OverflowClip));
        }

        [Fact]
        public async Task WithoutOverflow_NothingCarriesAClip()
        {
            var root = await RunningContentAsync("<div class='running'>plain running header text</div>");

            Assert.All(All(root), f => Assert.Null(f.OverflowClip));
        }
    }
}
