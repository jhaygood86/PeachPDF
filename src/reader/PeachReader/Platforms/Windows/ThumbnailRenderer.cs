using PeachDrawing.Core;
using PeachDrawing.Win2D;
using PeachReader.Core;

namespace PeachReader.WinUI;

/// <summary>Draws page thumbnails with Win2D on a software device, off the UI thread, so the page list never waits for the GPU the pages are using.</summary>
internal sealed class ThumbnailRenderer : IThumbnailRenderer
{
    private readonly object _gate = new();
    private Win2dRenderContext? _context;

    public Task<byte[]?> RenderAsync(ReaderDocument document, int pageIndex, int pixelWidth) => Task.Run(async () =>
    {
        try
        {
            var (width, height) = document.PageSizes[pageIndex];
            Win2dRenderContext context;
            lock (_gate) context = _context ??= Win2dRenderContext.CreateSoftware();

            using var composer = new Win2dComposer(context, width, height,
                new Win2dComposerOptions { Dpi = (float)(96.0 * pixelWidth / width), Background = PaintColor.White });
            lock (document.PaintGate) document.Source.Paint(pageIndex, composer.Canvas);
            composer.Finish();
            using var stream = new MemoryStream();
            await composer.SaveAsync(stream, Win2dImageFormat.Png).ConfigureAwait(false);
            return stream.ToArray();
        }
        catch (Exception)
        {
            // A page that cannot be drawn small is shown without a picture; the page itself reports its own trouble when it is opened.
            return null;
        }
    });
}
