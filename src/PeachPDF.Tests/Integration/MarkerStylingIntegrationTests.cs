using PeachPDF.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.Tests.TestSupport;
using System.Linq;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Verifies that author-set properties on <c>::marker</c> (color, font, content, direction) are
    /// actually read by the marker box itself and take visible effect - the gap this feature closes.
    /// Layout runs through the real <see cref="PdfSharpAdapter"/>/<see cref="GraphicsAdapter"/> (for
    /// accurate font metrics, matching real usage); paint verification for color/text assertions runs
    /// through <see cref="TestRecordingGraphics"/> instead, since it's the only way to introspect the
    /// exact <see cref="RColor"/> a brush/pen was created with (see its doc comment).
    /// </summary>
    public class MarkerStylingIntegrationTests
    {
        [Fact]
        public async Task MarkerColor_OverridesTextMarkerColor()
        {
            var (root, _) = await BuildAndLayout(Wrap(
                "<style>li::marker { color: red } li { color: blue }</style><ol><li id='li'>text</li></ol>"));
            var li = FindById(root, "li")!;
            var marker = li.Boxes.Single(b => b.IsMarkerPseudoElement);

            var g = new TestRecordingGraphics();
            await marker.Paint(g);

            var call = Assert.Single(g.DrawStringCalls);
            Assert.Equal(RColor.FromArgb(255, 0, 0), call.Color);
            Assert.NotEqual(RColor.FromArgb(0, 0, 255), call.Color);
        }

        [Fact]
        public async Task MarkerFontSize_OverridesTextMarkerFontSize()
        {
            var (root, _) = await BuildAndLayout(Wrap(
                "<style>li::marker { font-size: 30pt } li { font-size: 10pt }</style><ol><li id='li'>text</li></ol>"));
            var li = FindById(root, "li")!;
            var marker = (CssBoxMarker)li.Boxes.Single(b => b.IsMarkerPseudoElement);

            Assert.Equal(30, marker.ActualFont.Size, 1);
            Assert.NotEqual(li.ActualFont.Size, marker.ActualFont.Size);

            var g = new TestRecordingGraphics();
            await marker.Paint(g);

            var call = Assert.Single(g.DrawStringCalls);
            Assert.Equal(30, call.Font.Size, 1);
        }

        [Fact]
        public async Task MarkerContentString_OverridesProceduralNumbering_OnDiscDefaultList()
        {
            // Regression guard: content overrides must work on a <ul> (disc-default list-style-type)
            // just as well as on an <ol> (decimal default) - the override must fully replace the
            // shape branch of ResolveDefaultContent's dispatch, not just the text/counter branch.
            var (root, _) = await BuildAndLayout(Wrap(
                "<style>li::marker { content: \"→ \" }</style><ul><li id='li'>text</li></ul>"));
            var li = FindById(root, "li")!;
            var marker = (CssBoxMarker)li.Boxes.Single(b => b.IsMarkerPseudoElement);

            Assert.Equal("→ ", marker.Text);
            Assert.Null(marker.MarkerShape);
        }

        [Fact]
        public async Task MarkerContentString_OverridesProceduralNumbering()
        {
            var (root, _) = await BuildAndLayout(Wrap(
                "<style>li::marker { content: \"→ \" }</style><ol><li id='li'>text</li></ol>"));
            var li = FindById(root, "li")!;
            var marker = (CssBoxMarker)li.Boxes.Single(b => b.IsMarkerPseudoElement);

            // No auto "." suffix - the author's string is used verbatim.
            Assert.Equal("→ ", marker.Text);
        }

        [Fact]
        public async Task MarkerContentNone_SuppressesMarkerEntirely_OnDiscDefaultList()
        {
            var (root, _) = await BuildAndLayout(Wrap(
                "<style>li::marker { content: none }</style><ul><li id='li'>text</li></ul>"));
            var li = FindById(root, "li")!;
            var marker = (CssBoxMarker)li.Boxes.Single(b => b.IsMarkerPseudoElement);

            Assert.Null(marker.Text);
            Assert.Null(marker.MarkerShape);
            Assert.Null(marker.ContentImage);
        }

        [Fact]
        public async Task MarkerContentCounter_ResolvesPerItemAgainstListItemCounter()
        {
            var (root, _) = await BuildAndLayout(Wrap(
                "<style>li::marker { content: counter(list-item) \") \" }</style>" +
                "<ol><li id='a'>a</li><li id='b'>b</li><li id='c'>c</li></ol>"));

            var a = (CssBoxMarker)FindById(root, "a")!.Boxes.Single(b => b.IsMarkerPseudoElement);
            var b = (CssBoxMarker)FindById(root, "b")!.Boxes.Single(b => b.IsMarkerPseudoElement);
            var c = (CssBoxMarker)FindById(root, "c")!.Boxes.Single(b => b.IsMarkerPseudoElement);

            Assert.Equal("1) ", a.Text);
            Assert.Equal("2) ", b.Text);
            Assert.Equal("3) ", c.Text);
        }

        [Fact]
        public async Task MarkerContentNone_SuppressesMarkerEntirely()
        {
            var (root, _) = await BuildAndLayout(Wrap(
                "<style>#suppressed::marker { content: none }</style>" +
                "<ol><li id='suppressed'>a</li><li id='normal'>b</li></ol>"));

            var suppressed = (CssBoxMarker)FindById(root, "suppressed")!.Boxes.Single(b => b.IsMarkerPseudoElement);
            var normal = (CssBoxMarker)FindById(root, "normal")!.Boxes.Single(b => b.IsMarkerPseudoElement);

            Assert.Null(suppressed.Text);
            Assert.Null(suppressed.MarkerShape);
            Assert.Null(suppressed.ContentImage);
            Assert.Empty(suppressed.Words);

            Assert.Equal("2.", normal.Text);

            var g = new TestRecordingGraphics();
            await suppressed.Paint(g);
            Assert.Empty(g.Log);
        }

        [Fact]
        public async Task MarkerContentNormal_IsIndistinguishableFromNoRule()
        {
            var withRule = await BuildAndLayout(Wrap(
                "<style>li::marker { content: normal }</style><ol><li id='li'>text</li></ol>"));
            var withoutRule = await BuildAndLayout(Wrap("<ol><li id='li'>text</li></ol>"));

            var markerWith = (CssBoxMarker)FindById(withRule.root, "li")!.Boxes.Single(b => b.IsMarkerPseudoElement);
            var markerWithout = (CssBoxMarker)FindById(withoutRule.root, "li")!.Boxes.Single(b => b.IsMarkerPseudoElement);

            Assert.Equal(markerWithout.Text, markerWith.Text);
            Assert.Equal("1.", markerWith.Text);
        }

        [Fact]
        public async Task MarkerDirection_OverridesRtlPainting()
        {
            var (root, _) = await BuildAndLayout(Wrap(
                "<style>li::marker { direction: rtl }</style><ol><li id='li' style='direction: ltr'>text</li></ol>"));
            var li = FindById(root, "li")!;
            var marker = li.Boxes.Single(b => b.IsMarkerPseudoElement);

            var g = new TestRecordingGraphics();
            await marker.Paint(g);

            var call = Assert.Single(g.DrawStringCalls);
            Assert.True(call.Rtl);
        }

        [Fact]
        public async Task MarkerShapeColor_OverridesDiscColor()
        {
            var (root, _) = await BuildAndLayout(Wrap(
                "<style>li::marker { color: green }</style><ul><li id='li'>text</li></ul>"));
            var li = FindById(root, "li")!;
            var marker = (CssBoxMarker)li.Boxes.Single(b => b.IsMarkerPseudoElement);
            Assert.Equal("disc", marker.MarkerShape);

            var g = new TestRecordingGraphics();
            await marker.Paint(g);

            var pathCall = Assert.Single(g.Log.OfType<TestRecordingGraphics.DrawPathCall>());
            Assert.Equal(RColor.FromArgb(0, 128, 0), pathCall.Color);
        }

        [Fact]
        public async Task MarkerContentUrl_OverridesProceduralShape_WithNoShapePaint()
        {
            var svg = "data:image/svg+xml;base64," + System.Convert.ToBase64String(
                System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 10 10'><rect width='10' height='10'/></svg>"));

            var (root, _) = await BuildAndLayout(Wrap(
                $"<style>li::marker {{ content: url('{svg}') }}</style><ul><li id='li' style='list-style-type: disc'>text</li></ul>"));
            var li = FindById(root, "li")!;
            var marker = (CssBoxMarker)li.Boxes.Single(b => b.IsMarkerPseudoElement);

            Assert.Null(marker.MarkerShape);
            Assert.NotNull(marker.ContentImage);
        }

        [Fact]
        public async Task MarkerListStylePositionInside_MeasuresWithMarkerOwnFont()
        {
            // An "inside" marker flows as an ordinary first inline child - its measured word width
            // (which is what determines how much room it claims on line 1, pushing the rest of the
            // list item's own text right) must come from its own (possibly overridden) font, not the
            // <li>'s.
            var narrow = await BuildAndLayout(Wrap(
                "<ol style='list-style-position: inside'><li id='li'>text</li></ol>"));
            var wide = await BuildAndLayout(Wrap(
                "<style>li::marker { font-size: 40pt }</style>" +
                "<ol style='list-style-position: inside'><li id='li'>text</li></ol>"));

            var narrowMarker = (CssBoxMarker)FindById(narrow.root, "li")!.Boxes.Single(b => b.IsMarkerPseudoElement);
            var wideMarker = (CssBoxMarker)FindById(wide.root, "li")!.Boxes.Single(b => b.IsMarkerPseudoElement);

            var narrowWord = Assert.Single(narrowMarker.Words);
            var wideWord = Assert.Single(wideMarker.Words);

            Assert.True(wideWord.Width > narrowWord.Width);
        }

        // ─── Helpers ─────────────────────────────────────────────────────────────

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
