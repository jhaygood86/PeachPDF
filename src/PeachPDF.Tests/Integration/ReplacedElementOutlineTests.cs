using PeachDrawing.Core;
using PeachPDF.Tests.TestSupport;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// A replaced element takes <c>outline</c> like any other box (css-ui-4 §3.1): it is drawn after the
    /// element's own background, through the same outline-scope collection as every other outline.
    /// Asserted on the single ordered <see cref="TestRecordingGraphics.Log"/>.
    /// </summary>
    public class ReplacedElementOutlineTests
    {
        private static readonly PaintColor Ring = PaintColor.FromArgb(217, 74, 74);
        private static readonly PaintColor Blue = PaintColor.FromArgb(10, 20, 200);

        private const string Png =
            "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=";

        private const string Outline = "display: block; outline: 8pt solid rgb(217,74,74); width: 60pt; height: 40pt; margin: 20pt; background: rgb(10,20,200)";

        [Theory]
        [InlineData("<img style='{0}' src='" + Png + "'>")]
        [InlineData("<svg style='{0}' viewBox='0 0 10 10'><rect width='10' height='10' fill='green'/></svg>")]
        [InlineData("<object style='{0}' data='" + Png + "' type='image/png'></object>")]
        [InlineData("<math style='{0}'><mn>1</mn></math>")]
        [InlineData("<input type='checkbox' style='{0}'>")]
        public async Task ReplacedElement_DrawsItsOutline_OverItsOwnBackground(string template)
        {
            var g = await PaintAsync(template.Replace("{0}", Outline));

            var ring = g.Log.FindIndex(e => IsFillOf(e, Ring));
            var background = g.Log.FindIndex(e => IsFillOf(e, Blue));

            Assert.True(ring >= 0, "no outline was drawn around the replaced element");
            Assert.True(background >= 0, "the replaced element's background was not painted");
            Assert.True(ring > background, "the outline was painted under the element's own background");
        }

        [Fact]
        public async Task Image_WithoutAnOutline_DrawsNoRing()
        {
            var g = await PaintAsync($"<img style='width: 60pt; height: 40pt' src='{Png}'>");

            Assert.DoesNotContain(g.Log, e => IsFillOf(e, Ring));
        }

        [Fact]
        public async Task Image_OutlineOffset_PushesTheRingOutward()
        {
            var near = await PaintAsync($"<img style='{Outline}; outline-offset: 0pt' src='{Png}'>");
            var far = await PaintAsync($"<img style='{Outline}; outline-offset: 12pt' src='{Png}'>");

            Assert.True(RingExtent(far) > RingExtent(near) + 10, "outline-offset did not move the ring outward");
        }

        private static double RingExtent(TestRecordingGraphics g)
        {
            var max = 0.0;
            foreach (var e in g.Log)
            {
                if (e is TestRecordingGraphics.DrawRectCall r && r.PaintColor == Ring)
                    max = System.Math.Max(max, r.X + r.Width);
                else if (e is TestRecordingGraphics.DrawPathCall { Points.Count: > 0 } p && p.PaintColor == Ring)
                    max = System.Math.Max(max, p.Bounds.Right);
            }

            return max;
        }

        private static async Task<TestRecordingGraphics> PaintAsync(string body)
        {
            var (_, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(body));
            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintPage(container, g);
            return g;
        }

        private static bool IsFillOf(object entry, PaintColor color) => entry switch
        {
            TestRecordingGraphics.DrawRectCall r => r.PaintColor == color,
            TestRecordingGraphics.DrawPathCall { Stroked: false } p => p.PaintColor == color,
            TestRecordingGraphics.DrawPolygonCall p => p.PaintColor == color,
            _ => false
        };
    }
}
