using PeachDrawing.Core;
using PeachDrawing.Win2D;

namespace PeachDrawing.Win2D.Tests;

/// <summary>A software-rendered page and the helpers the tests read it back with.</summary>
internal sealed class Scene : IDisposable
{
    private PixelBuffer? _pixels;

    public Scene(Win2dRenderContext context, double width, double height, Win2dComposerOptions? options = null)
    {
        Context = context;
        Composer = new Win2dComposer(context, width, height, options);
    }

    public Win2dRenderContext Context { get; }

    public Win2dComposer Composer { get; }

    public Win2dCanvas Canvas => Composer.Canvas;

    public static Scene Create(double width = 40, double height = 40, double pixelsPerPoint = 1, Win2dTextEngine engine = Win2dTextEngine.PeachDrawing, Win2dComposerOptions? options = null)
    {
        var context = Win2dRenderContext.CreateSoftware(engine);
        context.PixelsPerPoint = pixelsPerPoint;
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

    public void Dispose()
    {
        Composer.Dispose();
        Context.Dispose();
    }
}
