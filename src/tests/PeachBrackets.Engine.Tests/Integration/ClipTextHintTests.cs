using PeachBrackets.Engine.Tests.TestSupport;
using PeachPDF.Tests.TestSupport;
using System.Threading.Tasks;
using Xunit;

namespace PeachBrackets.Engine.Tests.Integration
{
    /// <summary>
    /// A rounded <c>overflow: hidden</c> clip around content that cannot draw text is pushed with <c>Canvas.PushClipAroundShapes</c>, so a canvas that
    /// has to give a clip around text a surface of its own (Win2D, to blend ClearType against the pixels behind it) does not do that for a card's picture.
    /// Pushing it that way around text would cost the text its quality, so everything that is not provably text-free is pushed the ordinary way.
    /// </summary>
    public class ClipTextHintTests
    {
        private const string Card = "border-radius: 12pt; overflow: hidden; width: 200pt";

        private static async Task<TestRecordingGraphics> PaintAsync(string body)
        {
            var (_, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(body));
            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintPage(container, g);
            return g;
        }

        [Fact]
        public async Task A_rounded_clip_around_a_box_with_only_a_background_is_pushed_for_shapes()
        {
            var g = await PaintAsync($"<div style='{Card}'><div style='height: 40pt; background: rgb(10,20,200)'></div></div>");

            Assert.True(g.ShapeOnlyClipCount > 0, "the clip around a background-only child was not given the no-text hint");
            Assert.Empty(g.WordDrawStringCalls);
        }

        [Fact]
        public async Task A_rounded_clip_around_text_is_pushed_the_ordinary_way()
        {
            var g = await PaintAsync($"<div style='{Card}'><div style='height: 40pt'>caption</div></div>");

            Assert.NotEmpty(g.ClipPaths);
            Assert.Equal(0, g.ShapeOnlyClipCount);
            Assert.Contains(g.WordDrawStringCalls, c => c.Text == "caption");
        }

        [Fact]
        public async Task Text_nested_below_a_box_makes_the_clip_around_it_ordinary()
        {
            var g = await PaintAsync($"<div style='{Card}'><div><div><span>deep text</span></div></div></div>");

            Assert.Equal(0, g.ShapeOnlyClipCount);
            Assert.NotEmpty(g.ClipPaths);
        }

        private const string Png = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==";

        [Fact]
        public async Task A_rounded_clip_around_a_loaded_picture_is_pushed_for_shapes()
        {
            // The picture's own clip (an `overflow: hidden` rounded box around an image) holds a background, a border and the picture.
            var g = await PaintAsync($"<div style='{Card}'><img src='{Png}' style='display: block; width: 60pt; height: 40pt; border-radius: 6pt; overflow: hidden'></div>");

            Assert.True(g.ShapeOnlyClipCount > 0, "no clip around the picture was given the no-text hint");
            Assert.Equal(g.ClipPaths.Count, g.ShapeOnlyClipCount);
        }

        [Fact]
        public async Task A_picture_and_a_caption_in_one_card_are_clipped_each_as_what_they_are()
        {
            var g = await PaintAsync(
                $"<div style='{Card}'><div style='height: 40pt; background: rgb(10,20,200)'></div><div>caption</div></div>");

            Assert.True(g.ShapeOnlyClipCount > 0, "the background-only child lost the hint");
            Assert.True(g.ClipPaths.Count > g.ShapeOnlyClipCount, "the caption's clip was given the hint");
        }
    }
}
