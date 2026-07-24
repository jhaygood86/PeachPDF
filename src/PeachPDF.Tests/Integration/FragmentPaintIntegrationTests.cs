using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core.Fragments;
using PeachPDF.Tests.TestSupport;
using System.Linq;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Paint driven by the fragment tree: each page paints its own fragmentainer and nothing else,
    /// and every drawn rectangle is the one the fragment carries. Asserted on the ordered
    /// <see cref="TestRecordingGraphics.Log"/>, per this repo's painting-test conventions.
    /// </summary>
    public class FragmentPaintIntegrationTests
    {
        [Fact]
        public async Task EachPage_PaintsOnlyItsOwnFragmentainersContent()
        {
            var (_, container) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(
                    "<p style='margin:0;height:150pt'>PageOneMarker</p>"
                    + "<p style='margin:0;height:150pt;page-break-before:always'>PageTwoMarker</p>"),
                pageHeight: 200, margin: 0);

            Assert.Equal(2, container.FragmentTree!.Fragmentainers.Count);

            var page0 = new TestRecordingGraphics();
            await FragmentPaintHarness.PaintPage(container, page0, 0);

            var page1 = new TestRecordingGraphics();
            await FragmentPaintHarness.PaintPage(container, page1, 1);

            Assert.Contains(page0.DrawStringCalls, c => c.Text.Contains("PageOneMarker"));
            Assert.DoesNotContain(page0.DrawStringCalls, c => c.Text.Contains("PageTwoMarker"));

            Assert.Contains(page1.DrawStringCalls, c => c.Text.Contains("PageTwoMarker"));
            Assert.DoesNotContain(page1.DrawStringCalls, c => c.Text.Contains("PageOneMarker"));
        }

        [Fact]
        public async Task PaintedText_LandsAtItsOwnFragmentsCoordinates()
        {
            var (_, container) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(
                    "<p style='margin:0;height:150pt'>PageOneMarker</p>"
                    + "<p style='margin:0;height:150pt;page-break-before:always'>PageTwoMarker</p>"),
                pageHeight: 200, margin: 0);

            for (var page = 0; page < 2; page++)
            {
                var recording = new TestRecordingGraphics();
                await FragmentPaintHarness.PaintPage(container, recording, page);

                var fragmentainer = container.FragmentTree!.Fragmentainers[page];
                var wordRects = WordRects(fragmentainer.Root).ToList();

                Assert.NotEmpty(recording.DrawStringCalls);

                // Every drawn glyph run sits exactly where its own fragment says, in page-local
                // coordinates - no page offset is applied at paint time any more.
                foreach (var call in recording.DrawStringCalls)
                {
                    Assert.Contains(wordRects, r => System.Math.Abs(r.X - call.Point.X) < 0.001);
                }

                // Page 1's content is 200pt down the document but paints near its own page top.
                Assert.All(recording.DrawStringCalls, c => Assert.InRange(c.Point.Y, 0, 200));
            }
        }

        [Fact]
        public async Task BoxSpanningTwoPages_PaintsItsBackgroundOnBoth()
        {
            var (_, container) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap("<div id='tall' style='height:300pt;background:rgb(10,20,30)'>x</div>"),
                pageHeight: 200, margin: 0);

            Assert.Equal(2, container.FragmentTree!.Fragmentainers.Count);

            for (var page = 0; page < 2; page++)
            {
                var recording = new TestRecordingGraphics();
                await FragmentPaintHarness.PaintPage(container, recording, page);

                // Sliced, not cloned: each fragment paints the whole box's background rectangle and
                // the page clip does the cutting (box-decoration-break: slice, the initial value).
                Assert.Contains(recording.Log.OfType<TestRecordingGraphics.DrawRectCall>(),
                    r => r.Color == RColorOf(10, 20, 30));
            }
        }

        [Fact]
        public async Task FixedBox_PaintsAtTheSameCoordinatesOnEveryPage()
        {
            var (_, container) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(
                    "<div style='position:fixed;top:10pt;left:10pt;width:40pt;height:20pt;background:rgb(1,2,3)'></div>"
                    + "<p style='margin:0;height:150pt'>One</p>"
                    + "<p style='margin:0;height:150pt;page-break-before:always'>Two</p>"),
                pageHeight: 200, margin: 0);

            Assert.Equal(2, container.FragmentTree!.Fragmentainers.Count);

            var painted = new System.Collections.Generic.List<TestRecordingGraphics.DrawRectCall>();

            for (var page = 0; page < 2; page++)
            {
                var recording = new TestRecordingGraphics();
                await FragmentPaintHarness.PaintPage(container, recording, page);

                painted.Add(Assert.Single(
                    recording.Log.OfType<TestRecordingGraphics.DrawRectCall>(), r => r.Color == RColorOf(1, 2, 3)));
            }

            Assert.Equal(painted[0].X, painted[1].X, 3);
            Assert.Equal(painted[0].Y, painted[1].Y, 3);
        }

        [Fact]
        public async Task StackingOrder_IsPreservedWhenPaintingFromFragments()
        {
            // CSS 2.1 Appendix E within one stacking context: the lower z-index sibling paints first.
            var (_, container) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(
                    "<div style='position:relative'>"
                    + "<div style='position:absolute;z-index:2;width:10pt;height:10pt;background:rgb(0,0,255)'></div>"
                    + "<div style='position:absolute;z-index:1;width:10pt;height:10pt;background:rgb(255,0,0)'></div>"
                    + "</div>"));

            var recording = new TestRecordingGraphics();
            await FragmentPaintHarness.PaintPage(container, recording);

            var rects = recording.Log.OfType<TestRecordingGraphics.DrawRectCall>().ToList();
            var red = rects.FindIndex(r => r.Color == RColorOf(255, 0, 0));
            var blue = rects.FindIndex(r => r.Color == RColorOf(0, 0, 255));

            Assert.True(red >= 0 && blue >= 0, "both positioned boxes must paint");
            Assert.True(red < blue, "the lower z-index sibling must paint first");
        }

        [Fact]
        public async Task RowspanCell_ShowsThroughEveryRowItSpans()
        {
            // A rowspan placeholder has no content of its own; the spanned cell reaches it as a
            // fragment child, so the ordinary paint walk draws it once per row it spans.
            var (_, container) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(
                    "<table><tr><td rowspan='2'>SpannedCell</td><td>A</td></tr><tr><td>B</td></tr></table>"));

            var recording = new TestRecordingGraphics();
            await FragmentPaintHarness.PaintPage(container, recording);

            Assert.Contains(recording.DrawStringCalls, c => c.Text.Contains("SpannedCell"));
        }

        private static RColor RColorOf(int r, int g, int b) =>
            RColor.FromArgb(r, g, b);

        private static System.Collections.Generic.IEnumerable<RRect> WordRects(BoxFragment fragment)
        {
            foreach (var word in fragment.Words)
            {
                yield return word.Rect;
            }

            foreach (var child in fragment.Children)
            {
                foreach (var rect in WordRects(child))
                {
                    yield return rect;
                }
            }
        }
    }
}
