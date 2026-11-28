using Microsoft.Graphics.Canvas;
using PeachDrawing.Core;
using Windows.Foundation;
using Windows.UI;

namespace PeachDrawing.Win2D.Tests;

/// <summary><c>Win2dRenderContext.WrapBitmap</c>: a bitmap made on the device is drawn as it is, owned by whoever the caller says.</summary>
public class WrappedBitmapTests
{
    private static CanvasRenderTarget GreenTarget(Win2dRenderContext context, int width, int height)
    {
        var target = new CanvasRenderTarget(context.Device, width, height, 96);
        using var session = target.CreateDrawingSession();
        session.Clear(Color.FromArgb(255, 0, 200, 0));
        return target;
    }

    [Fact]
    public void AWrappedBitmap_IsDrawnOnTheDevice_WithItsOwnSize()
    {
        using var context = Win2dRenderContext.CreateSoftware();
        context.PixelsPerPoint = 1;
        using var scene = new Scene(context, 8, 8);
        using var bitmap = GreenTarget(context, 4, 4);
        using var image = context.WrapBitmap(bitmap);

        Assert.Equal((4, 4), (image.Width, image.Height));
        scene.Canvas.DrawImage(image, new PeachDrawing.Core.Rect(0, 0, 8, 8), ImageSampling.Nearest);

        Assert.Equal((0, 200, 0, 255), scene.Px(2, 2));
        Assert.Equal((0, 200, 0, 255), scene.Px(6, 6));
        Assert.Equal(1, scene.Context.Counts.WrappedBitmaps);
    }

    [Fact]
    public void ABorrowedBitmap_SurvivesTheImage_AndAnOwnedOneDoesNot()
    {
        using var context = Win2dRenderContext.CreateSoftware();
        var borrowed = GreenTarget(context, 2, 2);
        context.WrapBitmap(borrowed).Dispose();
        Assert.Equal(0, context.Counts.WrappedBitmaps);
        Assert.Equal(2u, borrowed.SizeInPixels.Width);
        borrowed.Dispose();

        var owned = GreenTarget(context, 2, 2);
        context.WrapBitmap(owned, ownsBitmap: true).Dispose();
        Assert.Equal(0, context.Counts.WrappedBitmaps);
        Assert.Throws<ObjectDisposedException>(() => _ = owned.SizeInPixels);
    }

    [Fact]
    public void PixelsAreReadBack_OnlyWhenAskedFor()
    {
        using var context = Win2dRenderContext.CreateSoftware();
        using var bitmap = GreenTarget(context, 2, 2);
        using var image = context.WrapBitmap(bitmap);

        var pixels = image.GetPixels()!.Value;
        Assert.Equal((2, 2), (pixels.Width, pixels.Height));
        Assert.Equal(200, pixels.PremultipliedRgba.Span[1]);
    }

    [Fact]
    public void ANullBitmap_IsRefused()
    {
        using var context = Win2dRenderContext.CreateSoftware();
        Assert.Throws<ArgumentNullException>(() => context.WrapBitmap(null!));
    }
}
