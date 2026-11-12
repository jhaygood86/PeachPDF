using PeachBrackets.Html;
using PeachBrackets.Html.Dom;
using PeachDrawing.Core;
using PeachDrawing.PDF;
using PeachPDF.Rendering;

namespace PeachBrackets.Engine.Tests.Integration
{
    /// <summary>
    /// HTML Standard 15.5.4: the first summary of a details is a list item whose marker is a disclosure triangle
    /// (closed or open), and an author can remove or restyle it like any other list marker.
    /// </summary>
    public class DetailsSummaryMarkerTests
    {
        private static CssBoxMarker? MarkerOf(CssBox root, string id) =>
            FindById(root, id)?.Boxes.SingleOrDefault(b => b.IsMarkerPseudoElement) as CssBoxMarker;

        [Fact]
        public async Task AClosedDetailsSummaryHasTheClosedDisclosureMarker()
        {
            var root = await BuildAndLayout("<details><summary id='s'>Closed</summary>hidden</details>");

            Assert.Equal("disclosure-closed", MarkerOf(root, "s")!.MarkerShape);
        }

        [Fact]
        public async Task AnOpenDetailsSummaryHasTheOpenDisclosureMarker()
        {
            var root = await BuildAndLayout("<details open><summary id='s'>Open</summary>shown</details>");

            Assert.Equal("disclosure-open", MarkerOf(root, "s")!.MarkerShape);
        }

        [Fact]
        public async Task ListStyleNoneRemovesTheMarker()
        {
            var root = await BuildAndLayout("<style>summary { list-style: none }</style><details open><summary id='s'>Open</summary>shown</details>");

            // A list-style-type of none leaves a marker box that paints nothing, as it does for a li.
            var marker = MarkerOf(root, "s");
            Assert.True(marker is null || (string.IsNullOrWhiteSpace(marker.Text) && marker.MarkerShape is null));
        }

        [Fact]
        public async Task TheMarkerCanBeStyledAndItsContentReplaced()
        {
            var root = await BuildAndLayout("<style>summary::marker { content: \"+ \" }</style><details><summary id='s'>Closed</summary>x</details>");

            Assert.Equal("+ ", MarkerOf(root, "s")!.Text);
        }

        [Fact]
        public async Task OnlyTheFirstSummaryOfADetailsHasAMarker()
        {
            var root = await BuildAndLayout("<details open><summary id='a'>One</summary><summary id='b'>Two</summary></details><summary id='c'>Loose</summary>");

            Assert.NotNull(MarkerOf(root, "a"));
            Assert.Null(MarkerOf(root, "b"));
            Assert.Null(MarkerOf(root, "c"));
        }

        [Fact]
        public async Task TheMarkerDoesNotLeakIntoTheContentOrItsLists()
        {
            var root = await BuildAndLayout("<details open><summary>S</summary><p id='p'>text</p><ul><li id='li'>item</li></ul></details>");

            Assert.Null(MarkerOf(root, "p"));
            Assert.Equal("shape", MarkerOf(root, "li") is { MarkerShape: not null } ? "shape" : "text");
        }

        private static async Task<CssBox> BuildAndLayout(string body)
        {
            var renderContext = new HtmlRenderContext { PixelsPerPoint = 1.0 };
            var container = new HtmlContainerInt(renderContext);
            await container.SetHtml($"<!DOCTYPE html><html><head></head><body>{body}</body></html>", null);

            var size = new Size(595, 842);
            container.PageSize = PeachBrackets.Utilities.Utils.ToPixels(size, 1.0);
            container.MaxSize = PeachBrackets.Utilities.Utils.ToPixels(size, 1.0);

            using var graphics = PdfCanvas.CreateMeasuring(renderContext, size);
            await container.PerformLayout(graphics);

            Assert.NotNull(container.Root);
            return container.Root!;
        }

        private static CssBox? FindById(CssBox box, string id)
        {
            if (box.HtmlTag?.TryGetAttribute("id", "") is { } value && value.Equals(id, StringComparison.OrdinalIgnoreCase))
                return box;
            foreach (var child in box.Boxes)
            {
                if (FindById(child, id) is { } found)
                    return found;
            }

            return null;
        }
    }
}
