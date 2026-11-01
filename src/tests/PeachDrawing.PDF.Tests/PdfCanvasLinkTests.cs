using System.Text;
using PeachDrawing.Core;
using PeachDrawing.PDF;

namespace PeachDrawing.PDF.Tests
{
    /// <summary>
    /// The links a <see cref="PdfCanvas"/> writes when asked through the <see cref="Canvas"/> API (<see cref="Canvas.AddLink"/> and its
    /// siblings) - the call a document's hosts make instead of reaching for the composer page.
    /// </summary>
    public class PdfCanvasLinkTests
    {
        private static string Write(PdfComposer composer)
        {
            using var stream = new MemoryStream();
            composer.Save(stream);
            return Encoding.Latin1.GetString(stream.ToArray());
        }

        [Fact]
        public void AWebLinkIsALinkAnnotationOverItsRectangle_InPointsFromTheBottomOfThePage()
        {
            // Two layout units to the point: the rectangle (20, 40, 100 x 20) is (10, 20, 50 x 10) points, 170 down a 200-point page.
            using var composer = new PdfComposer(new PdfRenderContext { PixelsPerPoint = 2 }, new PdfComposerOptions { CompressContentStreams = false });
            var page = composer.AddPage(300, 200);

            var handle = page.Canvas.AddLink(new Rect(20, 40, 100, 20), "https://example.com/");

            Assert.IsType<PdfLink>(handle); // what a tagged document attaches to a structure element
            var pdf = Write(composer);
            Assert.Contains("/Subtype /Link", pdf);
            Assert.Contains("/URI(https://example.com/)", pdf);
            Assert.Contains("/Rect [10 170 60 180]", pdf);
        }

        [Fact]
        public void ADestinationLinkLeadsToAPlaceNamedOnAnotherPagesCanvas()
        {
            using var composer = new PdfComposer(new PdfRenderContext(), new PdfComposerOptions { CompressContentStreams = false });
            var first = composer.AddPage(300, 200);
            var second = composer.AddPage(300, 200);

            first.Canvas.AddDestinationLink(new Rect(10, 10, 40, 12), "chapter-2");
            second.Canvas.AddDestination("chapter-2", 50);

            var pdf = Write(composer);
            Assert.Contains("/Subtype /Link", pdf);
            Assert.Contains("chapter-2", pdf);
            Assert.Contains("/Dests", pdf);
            Assert.Contains("/FitH 150", pdf); // 50 down a 200-point page
        }

        [Fact]
        public void ALinkOverARotatedShapeCarriesItsQuadPoints()
        {
            using var composer = new PdfComposer(new PdfRenderContext(), new PdfComposerOptions { CompressContentStreams = false });
            var page = composer.AddPage(300, 200);

            page.Canvas.AddLink(new Rect(0, 0, 20, 20), "x", [new PaintPoint(5, 0), new PaintPoint(20, 5), new PaintPoint(0, 15), new PaintPoint(15, 20)]);

            Assert.Contains("/QuadPoints", Write(composer));
        }

        [Fact]
        public void ATileOrAMeasuringCanvasHasNoPageSoItWritesNothingAndReturnsNull()
        {
            var context = new PdfRenderContext();
            using var measuring = PdfCanvas.CreateMeasuring(context, 100, 100);

            Assert.Null(measuring.AddLink(new Rect(0, 0, 1, 1), "x"));
            Assert.Null(measuring.AddDestinationLink(new Rect(0, 0, 1, 1), "x"));
            measuring.AddDestination("x", 1);
        }

        [Fact]
        public void APointerAreaIsIgnored_BecauseAPdfHasNoPointer()
        {
            using var composer = new PdfComposer(new PdfRenderContext(), new PdfComposerOptions { CompressContentStreams = false });
            var page = composer.AddPage(300, 200);
            var before = Write(composer).Length;

            page.Canvas.AddCursor(new Rect(10, 10, 50, 20), CursorShape.Pointer, [new CursorImage("https://example.com/c.png", 1, 2)]);

            Assert.Equal(before, Write(composer).Length);
        }
    }
}
