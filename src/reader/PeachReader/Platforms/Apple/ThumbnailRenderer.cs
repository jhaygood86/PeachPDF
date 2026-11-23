using CoreGraphics;
using PeachDrawing.CoreGraphics;
using PeachReader.Core;
using UIKit;

namespace PeachReader.Apple;

/// <summary>Draws page thumbnails with CoreGraphics into a bitmap, off the interface thread, so the page list never waits for the pages being drawn.</summary>
internal sealed class ThumbnailRenderer : IThumbnailRenderer
{
    private readonly object _gate = new();
    private CoreGraphicsRenderContext? _context;

    public Task<byte[]?> RenderAsync(ReaderDocument document, int pageIndex, int pixelWidth) => Task.Run<byte[]?>(() =>
    {
        try
        {
            var (width, height) = document.PageSizes[pageIndex];
            if (width <= 0 || height <= 0 || pixelWidth <= 0) return null;
            int pixelHeight = Math.Max(1, (int)Math.Ceiling(pixelWidth * height / width));
            CoreGraphicsRenderContext context;
            lock (_gate) context = _context ??= new CoreGraphicsRenderContext();

            using var colorSpace = CGColorSpace.CreateSrgb();
            using var bitmap = new CGBitmapContext(IntPtr.Zero, pixelWidth, pixelHeight, 8, 0, colorSpace, CGBitmapFlags.PremultipliedFirst | CGBitmapFlags.ByteOrder32Little);
            bitmap.SetFillColor(new CGColor(1, 1, 1, 1));
            bitmap.FillRect(new CGRect(0, 0, pixelWidth, pixelHeight));
            // Y down from the top-left, as the canvas draws.
            bitmap.TranslateCTM(0, pixelHeight);
            bitmap.ScaleCTM(1, -1);
            bitmap.ScaleCTM((nfloat)(pixelWidth / width), (nfloat)(pixelHeight / height));
            using (var target = CoreGraphicsCanvas.FromContext(context, bitmap, new PeachDrawing.Core.Rect(0, 0, width, height), originAtBottomLeft: false))
            {
                lock (document.PaintGate) document.Source.Paint(pageIndex, target);
            }

            using var image = bitmap.ToImage();
            if (image is null) return null;
            using var png = UIImage.FromImage(image).AsPNG();
            return png?.ToArray();
        }
        catch (Exception)
        {
            // A page that cannot be drawn small is shown without a picture; the page itself reports its own trouble when it is opened.
            return null;
        }
    });
}
