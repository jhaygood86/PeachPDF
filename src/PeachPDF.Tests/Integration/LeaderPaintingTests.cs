using PeachPDF.Adapters;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.Tests.TestSupport;
using System.Linq;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Painting tests for <c>leader()</c> (css-content-3 §6), asserting the actual sequence of calls
    /// made to the <c>RGraphics</c> adapter layer via <see cref="TestRecordingGraphics"/> - per this
    /// repo's painting-test convention (CLAUDE.md), not a content-stream substring check. Drives the
    /// real paint pipeline off a real layout (<see cref="FragmentPaintHarness"/>), same as
    /// <c>TransformIntegrationTests</c>.
    /// </summary>
    public class LeaderPaintingTests
    {
        [Fact]
        public async Task PaintLeader_Dotted_TilesDotGlyphsToFillWidth()
        {
            var html = Wrap("""
                <div id="line" style="width:100pt;">
                    <span id="leader" style="content: leader(dotted);"></span>
                </div>
                """);

            var (root, container) = await BuildAndLayout(html);
            var leaderBox = FindById(root, "leader")!;

            var spy = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, leaderBox, spy);

            var call = Assert.Single(spy.DrawStringCalls);
            Assert.NotEmpty(call.Text);
            Assert.All(call.Text, c => Assert.Equal('.', c));
        }

        [Fact]
        public async Task PaintLeader_NoArgument_DefaultsToDottedGlyphs()
        {
            // css-content-3: leader(<leader-type>?) - the argument is optional and defaults to dotted.
            var html = Wrap("""
                <div id="line" style="width:100pt;">
                    <span id="leader" style="content: leader();"></span>
                </div>
                """);

            var (root, container) = await BuildAndLayout(html);
            var leaderBox = FindById(root, "leader")!;

            var spy = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, leaderBox, spy);

            var call = Assert.Single(spy.DrawStringCalls);
            Assert.NotEmpty(call.Text);
            Assert.All(call.Text, c => Assert.Equal('.', c));
        }

        [Fact]
        public async Task PaintLeader_CustomStringPattern_TilesThatPatternNotDots()
        {
            var html = Wrap("""
                <div id="line" style="width:100pt;">
                    <span id="leader" style='content: leader("-");'></span>
                </div>
                """);

            var (root, container) = await BuildAndLayout(html);
            var leaderBox = FindById(root, "leader")!;

            var spy = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, leaderBox, spy);

            var call = Assert.Single(spy.DrawStringCalls);
            Assert.NotEmpty(call.Text);
            Assert.All(call.Text, c => Assert.Equal('-', c));
        }

        [Fact]
        public async Task PaintLeader_Solid_DrawsOneFilledRuleNotTiledText()
        {
            var html = Wrap("""
                <div id="line" style="width:100pt;">
                    <span id="leader" style="content: leader(solid);"></span>
                </div>
                """);

            var (root, container) = await BuildAndLayout(html);
            var leaderBox = FindById(root, "leader")!;

            var spy = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, leaderBox, spy);

            Assert.Empty(spy.DrawStringCalls);
            var rectCall = Assert.Single(spy.Log.OfType<TestRecordingGraphics.DrawRectCall>());
            Assert.True(rectCall.Width > 0);
            Assert.True(rectCall.Height > 0);
        }

        [Fact]
        public async Task PaintLeader_Space_PaintsNothing()
        {
            var html = Wrap("""
                <div id="line" style="width:100pt;">
                    <span id="leader" style="content: leader(space);"></span>
                </div>
                """);

            var (root, container) = await BuildAndLayout(html);
            var leaderBox = FindById(root, "leader")!;

            var spy = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, leaderBox, spy);

            Assert.Empty(spy.Log);
        }

        private static string Wrap(string body) =>
            $"<!DOCTYPE html><html><head></head><body>{body}</body></html>";

        private static async Task<(CssBox root, HtmlContainerInt container)> BuildAndLayout(string html)
        {
            var adapter = new PdfSharpAdapter();
            adapter.PixelsPerPoint = 1.0;
            var container = new HtmlContainerInt(adapter);
            await container.SetHtml(html, null);

            var size = new XSize(595, 842);
            container.PageSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);
            container.MaxSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);

            var measure = XGraphics.CreateMeasureContext(size, XGraphicsUnit.Point, XPageDirection.Downwards);
            using var graphics = new GraphicsAdapter(adapter, measure, 1.0);
            await container.PerformLayout(graphics);

            Assert.NotNull(container.Root);
            return (container.Root!, container);
        }

        private static CssBox? FindById(CssBox box, string id)
        {
            var val = box.HtmlTag?.TryGetAttribute("id", "");
            if (val != null && val.Equals(id, System.StringComparison.OrdinalIgnoreCase))
                return box;
            foreach (var child in box.Boxes)
            {
                var found = FindById(child, id);
                if (found != null) return found;
            }
            return null;
        }
    }
}
