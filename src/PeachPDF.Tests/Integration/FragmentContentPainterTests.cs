using PeachPDF.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Paint.Content;
using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.Tests.TestSupport;
using System.Linq;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// The per-box-type paint dispatch: <see cref="FragmentContentPainters.For"/> picks the painter,
    /// and the ones the other paint suites don't already drive (<c>&lt;iframe&gt;</c>,
    /// <c>&lt;hr&gt;</c>) draw what they should. Asserted on the ordered
    /// <see cref="TestRecordingGraphics.Log"/>, per this repo's painting-test conventions.
    /// </summary>
    public class FragmentContentPainterTests
    {
        [Fact]
        public async Task For_PlainBox_HasNoContentPainter()
        {
            var (root, _) = await BuildAndLayout(Wrap("<div id='el'>x</div>"));

            Assert.Null(FragmentContentPainters.For(FindById(root, "el")!));
        }

        [Theory]
        [InlineData("<img id='el' src='missing.png'>", typeof(ImageFragmentPainter))]
        [InlineData("<object id='el' data='missing.png'>fallback</object>", typeof(ObjectFragmentPainter))]
        [InlineData("<svg id='el' width='10' height='10'></svg>", typeof(SvgFragmentPainter))]
        [InlineData("<iframe id='el' width='40' height='30'></iframe>", typeof(FrameFragmentPainter))]
        [InlineData("<hr id='el'>", typeof(HrFragmentPainter))]
        public async Task For_ReplacedBox_PicksItsOwnPainter(string body, System.Type expected)
        {
            var (root, _) = await BuildAndLayout(Wrap(body));

            Assert.IsType(expected, FragmentContentPainters.For(FindById(root, "el")!));
        }

        [Fact]
        public async Task Iframe_PaintsItsOwnBoxOnly_WithNoEmbeddedContent()
        {
            // A static PDF cannot embed a browsing context, so an <iframe> paints as an empty box:
            // its own decorations, and nothing inside.
            var (root, container) = await BuildAndLayout(Wrap(
                "<iframe id='el' style='display:block; width:40pt; height:30pt;"
                + " background:rgb(10,20,30); border:2pt solid rgb(1,2,3)'></iframe>"));

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, FindById(root, "el")!, g);

            Assert.Contains(g.Log.OfType<TestRecordingGraphics.DrawRectCall>(),
                r => r.Color == RColor.FromArgb(10, 20, 30));
            Assert.Empty(g.DrawImageCalls);
            Assert.Empty(g.DrawStringCalls);
        }

        [Fact]
        public async Task Hr_TallerThanTheRule_FillsItsBackground()
        {
            // An <hr> tall enough to have an interior fills it with background-color before drawing
            // the border sides that make up the rule itself.
            var (root, container) = await BuildAndLayout(Wrap(
                "<hr id='el' style='height: 20pt; background: rgb(10,20,30); border: none'>"));

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, FindById(root, "el")!, g);

            Assert.Contains(g.Log.OfType<TestRecordingGraphics.DrawRectCall>(),
                r => r.Color == RColor.FromArgb(10, 20, 30));
        }

        // ── Helpers ───────────────────────────────────────────────────────────────

        private static string Wrap(string body) =>
            $"<!DOCTYPE html><html><head></head><body>{body}</body></html>";

        private static async Task<(CssBox root, HtmlContainerInt container)> BuildAndLayout(string html)
        {
            var adapter = new PdfSharpAdapter { PixelsPerPoint = 1.0 };
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
