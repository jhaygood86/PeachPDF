using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Tests.TestSupport;
using System.Linq;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// An <c>outside</c> marker hangs off its item's inline-start edge (CSS Lists 3 §3.1), in whatever room
    /// the item's own margin or padding leaves. A list whose items sit at the content edge - no padding, or a
    /// <c>display: contents</c> list whose own box, and the indent with it, is gone - therefore hangs into the
    /// page margin, and the page-level clip that starts at the content-area edge deleted the marker outright:
    /// <c>&lt;ol style="display:contents"&gt;&lt;li&gt;a&lt;li&gt;b&lt;/ol&gt;</c> drew "a" and "b" with no numbers.
    /// The clip is widened by however far a marker overhangs it, the way it already is for an outline.
    /// </summary>
    /// <remarks>
    /// These paint through <see cref="FragmentPaintHarness.PaintPage"/> and not <c>PaintBox</c>: only the
    /// page paint pushes the page clip, and the clip is the subject. Every existing list test indents its list
    /// by 40pt, which is why none of them saw a clipped marker.
    /// </remarks>
    public class OutsideMarkerPageClipTests
    {
        private const double PageWidth = 300;
        private const double Margin = 20;

        private static async Task<(TestRecordingGraphics G, RRect PageClip)> PaintAsync(string body)
        {
            var (_, container) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(body), pageWidth: PageWidth, pageHeight: 200, margin: Margin);

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintPage(container, g);

            var pageClip = g.Log.OfType<TestRecordingGraphics.PushClipCall>().First().Rect;
            return (g, pageClip);
        }

        [Fact]
        public async Task TopLevelContentsOrderedList_DrawsItsNumbersInsideTheWidenedClip()
        {
            var (g, pageClip) = await PaintAsync("<ol style='display:contents'><li>a<li>b</ol>");

            foreach (var number in new[] { "1.", "2." })
            {
                var call = Assert.Single(g.DrawStringCalls, c => c.Text.Trim() == number);

                // The marker really does hang past the content edge - the fixture would prove nothing if it
                // did not - and the clip has been widened to take it in.
                Assert.True(call.Point.X < Margin, $"'{number}' (X={call.Point.X}) should hang into the margin");
                Assert.True(call.Point.X >= pageClip.Left - 0.001,
                    $"'{number}' (X={call.Point.X}) is left of the page clip ({pageClip.Left}) and would be cut off");
            }
        }

        [Fact]
        public async Task ContentsUnorderedList_DrawsItsBulletsInsideTheWidenedClip()
        {
            var (g, pageClip) = await PaintAsync("<ul style='display:contents'><li>a<li>b</ul>");

            var bullets = g.Log.OfType<TestRecordingGraphics.DrawPathCall>().ToList();

            Assert.Equal(2, bullets.Count);
            Assert.All(bullets, b =>
            {
                Assert.True(b.Bounds.Left < Margin, "the bullet should hang into the margin");
                Assert.True(b.Bounds.Left >= pageClip.Left - 0.001, "the bullet must not be clipped off");
            });
        }

        [Fact]
        public async Task ListWithNoPadding_DrawsItsHangingNumberInsideTheClip()
        {
            var (g, pageClip) = await PaintAsync("<ol style='margin:0;padding:0'><li>a</li></ol>");

            var call = Assert.Single(g.DrawStringCalls, c => c.Text.Trim() == "1.");

            Assert.True(call.Point.X < Margin);
            Assert.True(call.Point.X >= pageClip.Left - 0.001);
        }

        [Fact]
        public async Task ListWithItsUsualIndent_LeavesThePageClipAtTheContentArea()
        {
            // The control: nothing hangs past the content area, so the clip is exactly the content area
            // and a widening that always happened would be caught here.
            var (_, pageClip) = await PaintAsync("<ol style='margin:0;padding-left:40pt'><li>a</li></ol>");

            Assert.Equal(Margin, pageClip.Left, 3);
            Assert.Equal(PageWidth - Margin, pageClip.Right, 3);
        }

        [Fact]
        public async Task MarkerHungFarOffTheSheet_WidensTheClipNoFurtherThanTheSheetEdge()
        {
            // The visually-hidden idiom: a list moved thousands of points off to the left.
            var (_, pageClip) = await PaintAsync(
                "<ul style='position:absolute;left:-9999pt;margin:0;padding:0'><li>a</li></ul>");

            Assert.True(pageClip.Left >= 0, $"the clip ({pageClip.Left}) must not extend past the sheet's own edge");
        }

        [Fact]
        public async Task MarkerInsideAnOverflowHiddenAncestor_IsStillClippedByIt()
        {
            // Only the page-level clip widens: the ancestor's own clip is pushed after it and still applies,
            // so a marker hanging out of an overflow:hidden box is cut by that box, not shown in the margin.
            var (g, _) = await PaintAsync(
                "<div style='overflow:hidden'><ol style='margin:0;padding:0'><li>a</li></ol></div>");

            var pushes = g.Log.OfType<TestRecordingGraphics.PushClipCall>().Select(c => c.Rect).ToList();
            var call = Assert.Single(g.DrawStringCalls, c => c.Text.Trim() == "1.");

            // A clip narrower than the page clip was pushed for the div, and it starts right of the marker.
            Assert.Contains(pushes.Skip(1), r => r.Left > call.Point.X);
        }

        [Fact]
        public async Task ListStyleNone_DoesNotWidenTheClip()
        {
            // A marker that draws nothing has no ink to reach for.
            var (_, pageClip) = await PaintAsync("<ol style='margin:0;padding:0;list-style:none'><li>a</li></ol>");

            Assert.Equal(Margin, pageClip.Left, 3);
        }

        [Fact]
        public async Task ImageMarker_WidensTheClipToTakeInItsImage()
        {
            const string Gif = "data:image/gif;base64,R0lGODlhAQABAIAAAAUEBAAAACwAAAAAAQABAAACAkQBADs=";

            var (g, pageClip) = await PaintAsync(
                $"<ul style='margin:0;padding:0;list-style-image:url({Gif})'><li style='min-height:20pt'>a</li></ul>");

            var image = Assert.Single(g.DrawImageCalls);

            Assert.True(image.DestRect.Left < Margin, "the image marker should hang into the margin");
            Assert.True(image.DestRect.Left >= pageClip.Left - 0.001);
        }
    }
}
