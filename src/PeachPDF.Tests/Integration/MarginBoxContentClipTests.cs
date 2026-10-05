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
        public async Task FlowedInlineBlockClipper_ClipsToItsLineRectangle_NotToZeroBounds()
        {
            // An inline-block that fits on its line is flowed into it and never given a Location or Size of its
            // own, so its Bounds are empty; the page emitter takes the union of its line rectangles instead.
            var root = await RunningContentAsync(
                "<div class='running'>before <span style='display:inline-block; overflow:hidden'>short text</span> after</div>");

            var inside = All(root).Where(f => f.Words.Any(w => w.Word.Text == "short")).ToList();

            Assert.NotEmpty(inside);
            Assert.All(inside, f =>
            {
                Assert.NotNull(f.OverflowClip);
                Assert.True(f.OverflowClip!.Value.Width > 0 && f.OverflowClip!.Value.Height > 0,
                    $"the clip ({f.OverflowClip}) must have area, or every word inside is clipped away");
            });
        }

        [Fact]
        public async Task RoundedClipper_GivesItsContentTheCornerCurve()
        {
            var root = await RunningContentAsync(
                "<div class='running'><div style='overflow:hidden; width:60pt; border-radius:8pt; white-space:nowrap'>"
                + "a long line of running header text</div></div>");

            var inside = All(root).Where(f => f.Words.Count > 0 && f.OverflowClip is not null).ToList();

            Assert.NotEmpty(inside);
            Assert.All(inside, f =>
            {
                Assert.NotNull(f.OverflowClipCurve);
                Assert.NotNull(f.OverflowClipBasis);
            });
        }

        [Fact]
        public async Task OneAxisClipper_OpensTheOtherAxisAndCarriesNoBasisToResnap()
        {
            // overflow-x: clip beside a visible overflow-y clips only horizontally (css-overflow-3 §3.2).
            var root = await RunningContentAsync(
                "<div class='running'><div style='overflow-x:clip; overflow-y:visible; width:60pt; border-radius:8pt; white-space:nowrap'>"
                + "a long line of running header text</div></div>");

            var inside = All(root).Where(f => f.Words.Count > 0 && f.OverflowClip is not null).ToList();

            Assert.NotEmpty(inside);
            Assert.All(inside, f =>
            {
                Assert.True(f.OverflowClip!.Value.Height > 1e5, "the unclipped axis is opened out");
                Assert.Null(f.OverflowClipBasis);
                Assert.Null(f.OverflowClipCurve);
            });
        }

        [Fact]
        public async Task NestedClippers_EachGivesItsOwnContentItsOwnEdge()
        {
            // As in the page: a fragment carries its nearest clipper's edge, the outer one stays in force because
            // the painter has pushed it around the whole subtree.
            var root = await RunningContentAsync(
                "<div class='running'><div style='overflow:hidden; width:40pt'>"
                + "<div id='inner' style='overflow:hidden; width:80pt; white-space:nowrap'>a long line of running header text</div></div></div>");

            var inner = All(root).First(f => f.Box.Overflow.Value == PeachPDF.CSS.Overflow.Hidden
                                              && f.Children.Any(c => c.Words.Count > 0));
            var text = All(inner).Where(f => !ReferenceEquals(f, inner) && f.Words.Count > 0).ToList();

            Assert.NotEmpty(text);
            Assert.All(text, f => Assert.Equal(inner.Rect.Width, f.OverflowClip!.Value.Width, 1));
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
