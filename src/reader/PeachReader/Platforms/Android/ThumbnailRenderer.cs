using PeachDrawing.PDF;
using PeachDrawing.Skia;
using PeachReader.Core;
using SkiaSharp;

namespace PeachReader.Droid;

/// <summary>Draws page thumbnails with Skia on the processor, off the interface thread, so the page list never waits for the graphics hardware the pages are using.</summary>
internal sealed class ThumbnailRenderer : IThumbnailRenderer
{
    private readonly object _gate = new();
    private SkiaRenderContext? _context;

    public Task<byte[]?> RenderAsync(ReaderDocument document, int pageIndex, int pixelWidth) => Task.Run<byte[]?>(() =>
    {
        try
        {
            var (width, height) = document.PageSizes[pageIndex];
            int pixelHeight = Math.Max(1, (int)Math.Ceiling(pixelWidth * height / width));
            SkiaRenderContext context;
            lock (_gate) context = _context ??= new SkiaRenderContext();

            using var surface = SKSurface.Create(new SKImageInfo(pixelWidth, pixelHeight, SKColorType.Rgba8888, SKAlphaType.Premul));
            var canvas = surface.Canvas;
            canvas.Clear(SKColors.White);
            canvas.Scale((float)(pixelWidth / width), (float)(pixelHeight / height));
            using (var target = SkiaCanvas.FromCanvas(context, canvas, new PeachDrawing.Core.Rect(0, 0, width, height)))
            {
                lock (document.PaintGate) document.Source.Paint(pageIndex, target);
            }

            using var image = surface.Snapshot();
            using var data = image.Encode(SKEncodedImageFormat.Png, 90);
            return data.ToArray();
        }
        catch (Exception)
        {
            // A page that cannot be drawn small is shown without a picture; the page itself reports its own trouble when it is opened.
            return null;
        }
    });
}
