using PeachDrawing.Core;
using PeachDrawing.Skia;
using SkiaSharp;

namespace PeachDrawing.Skia.Tests;

/// <summary>A CPU surface the tests supply (the package never makes one) with a composer over it, and the helpers the tests read it back with.</summary>
internal sealed class Scene : IDisposable
{
    private PixelBuffer? _pixels;

    public Scene(SkiaRenderContext context, double width, double height, SkiaComposerOptions? options = null)
    {
        Context = context;
        Surface = SKSurface.Create(new SKImageInfo((int)Math.Ceiling(width * context.PixelsPerPoint), (int)Math.Ceiling(height * context.PixelsPerPoint), SKColorType.Rgba8888, SKAlphaType.Premul));
        Composer = new SkiaComposer(context, Surface, width, height, options);
    }

    public SkiaRenderContext Context { get; }

    public SKSurface Surface { get; }

    public SkiaComposer Composer { get; }

    public SkiaCanvas Canvas => Composer.Canvas;

    public static Scene Create(double width = 40, double height = 40, double pixelsPerPoint = 1, SkiaTextEngine engine = SkiaTextEngine.PeachDrawing,
        SkiaImageCodec codec = SkiaImageCodec.PeachImage, SkiaComposerOptions? options = null)
    {
        var context = new SkiaRenderContext(engine, codec) { PixelsPerPoint = pixelsPerPoint };
        return new Scene(context, width, height, options);
    }

    public static async Task<Scene> WithFontAsync(double width = 200, double height = 60)
    {
        var scene = Create(width, height);
        await using var stream = File.OpenRead(PeachPDF.Tests.TestSupport.BundledFonts.Ttf);
        await scene.Context.AddFont(stream, "Source Sans 3");
        return scene;
    }

    public Brush Solid(int r, int g, int b, int a = 255) => Context.GetSolidBrush(PaintColor.FromArgb(a, r, g, b));

    public Pen Pen(int r, int g, int b, double width)
    {
        var pen = Context.GetPen(PaintColor.FromArgb(255, r, g, b));
        pen.Width = width;
        return pen;
    }

    /// <summary>The pixel at (x, y) as premultiplied RGBA; reading finishes the drawing.</summary>
    public (int R, int G, int B, int A) Px(int x, int y)
    {
        _pixels ??= Composer.ToPixelBuffer();
        var span = _pixels.Value.PremultipliedRgba.Span;
        var i = (y * _pixels.Value.Width + x) * 4;
        return (span[i], span[i + 1], span[i + 2], span[i + 3]);
    }

    public bool IsClear(int x, int y) => Px(x, y).A == 0;

    public bool IsRed(int x, int y) => Px(x, y) is { R: > 200, G: < 40, B: < 40, A: > 200 };

    public bool IsBlue(int x, int y) => Px(x, y) is { R: < 40, G: < 40, B: > 200, A: > 200 };

    public int OpaquePixels()
    {
        _pixels ??= Composer.ToPixelBuffer();
        var span = _pixels.Value.PremultipliedRgba.Span;
        var count = 0;
        for (var i = 3; i < span.Length; i += 4)
        {
            if (span[i] > 0)
                count++;
        }

        return count;
    }

    /// <summary>Adds a closed rectangle to <paramref name="path"/> (coordinates in points).</summary>
    public static void AddRectangle(GraphicsPath path, double x, double y, double width, double height)
    {
        path.Start(x, y);
        path.LineTo(x + width, y);
        path.LineTo(x + width, y + height);
        path.LineTo(x, y + height);
        path.CloseFigure();
    }

    /// <summary>A small PNG, encoded by Skia: red on the left half, blue on the right.</summary>
    public static byte[] TwoColourPng(int width = 8, int height = 4)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(bitmap))
        {
            using var red = new SKPaint { Color = SKColors.Red };
            using var blue = new SKPaint { Color = SKColors.Blue };
            canvas.DrawRect(0, 0, width / 2f, height, red);
            canvas.DrawRect(width / 2f, 0, width / 2f, height, blue);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    public void Dispose()
    {
        Composer.Dispose();
        Surface.Dispose();
        Context.Dispose();
    }
}
