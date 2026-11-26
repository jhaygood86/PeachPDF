using PeachDrawing.Core;

namespace PeachDrawing.PDF.Tests;

public class PdfOffscreenSurfaceTests
{
    [Fact]
    public void ADocumentWriterHasNoBitmapToKeepDrawingInto()
    {
        using var composer = new PdfComposer(new PdfRenderContext(), new PdfComposerOptions { CompressContentStreams = false });
        var page = composer.AddPage(100, 100);

        Assert.Null(page.Canvas.CreateOffscreenSurface(10, 10));
        Assert.False(page.Canvas.SupportsCompositeOperators);
    }

    [Fact]
    public void AMeasuringCanvasHasNoBitmapEither()
    {
        using var measuring = PdfCanvas.CreateMeasuring(new PdfRenderContext(), 100, 100);

        Assert.Null(measuring.CreateOffscreenSurface(10, 10));
    }
}
