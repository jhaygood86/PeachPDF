using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;
using PeachDrawing.Core;
using PeachPDF.Tests.TestSupport;
using System.Numerics;
using Windows.Graphics.DirectX;

namespace PeachDrawing.Win2D.Tests;

/// <summary>
/// Text drawn over a render target the canvas can sample is blended on the GPU by a pixel shader. It has to look like the text the read-back path draws,
/// stay where it was drawn in the order things were drawn, and release everything it makes.
/// </summary>
public class GpuTextTests
{
    private const double Width = 320;
    private const double Height = 90;
    private const string Sample = "Hamburgefonstiv quick brown fox";

    private static async Task<Win2dRenderContext> ContextAsync(Win2dTextRendering mode = Win2dTextRendering.Lcd)
    {
        var context = Win2dRenderContext.CreateSoftware();
        context.TextRendering = mode;
        await using var stream = File.OpenRead(BundledFonts.Ttf);
        await context.AddFont(stream, "Source Sans 3");
        return context;
    }

    private static CanvasRenderTarget Target(Win2dRenderContext context, float dpi = 96) =>
        new(context.Device, (float)Width, (float)Height, dpi, DirectXPixelFormat.B8G8R8A8UIntNormalized, CanvasAlphaMode.Ignore);

    private static void Gradient(CanvasDrawingSession session)
    {
        using var brush = new CanvasLinearGradientBrush(session, Windows.UI.Color.FromArgb(255, 250, 200, 120), Windows.UI.Color.FromArgb(255, 40, 80, 170))
        {
            StartPoint = new Vector2(0, 0),
            EndPoint = new Vector2((float)Width, (float)Height),
        };
        session.FillRectangle(0, 0, (float)Width, (float)Height, brush);
    }

    // Draws the scene either the way a host's session has always been drawn (the pixels behind each word read back) or with the GPU blend.
    private static byte[] Render(Win2dRenderContext context, bool gpu, Action<Win2dCanvas, Font> scene, float dpi = 96)
    {
        using var target = Target(context, dpi);
        using (var session = target.CreateDrawingSession())
        {
            Gradient(session);
            using var canvas = gpu
                ? Win2dCanvas.FromRenderTarget(context, target, session, new Rect(0, 0, Width, Height))
                : Win2dCanvas.FromSession(context, session, new Rect(0, 0, Width, Height));
            scene(canvas, context.GetFont("Source Sans 3", 16, PaintFontStyle.Regular)!);
        }

        return target.GetPixelBytes();
    }

    private static (double Mean, int Max, double Within4) Compare(byte[] expected, byte[] actual)
    {
        Assert.Equal(expected.Length, actual.Length);
        long sum = 0;
        var max = 0;
        long near = 0, count = 0;
        for (var i = 0; i < expected.Length; i += 4)
        {
            for (var c = 0; c < 3; c++)
            {
                var d = Math.Abs(expected[i + c] - actual[i + c]);
                sum += d;
                count++;
                if (d > max) max = d;
                if (d <= 4) near++;
            }
        }

        return ((double)sum / count, max, (double)near / count);
    }

    private static void Words(Win2dCanvas canvas, Font font)
    {
        canvas.DrawString(Sample, font, PaintColor.FromArgb(255, 20, 30, 120), new PaintPoint(6, 6), new Core.Size(300, 20));
        canvas.DrawString(Sample, font, PaintColor.White, new PaintPoint(6, 32), new Core.Size(300, 20));
        canvas.DrawString("Second line, 123.45", font, PaintColor.FromArgb(255, 160, 20, 20), new PaintPoint(6.5, 58.25), new Core.Size(300, 20));
    }

    [Fact]
    public async Task TextInsideAGeometryClip_IsDrawnOverTheClipsOwnFill_NotOverWhatWasInTheTargetBefore()
    {
        // A card with rounded corners clips with a geometry, which Direct2D draws into a layer until the clip is popped: the card's fill is not in the
        // render target yet, so text blended against the target came out as a rectangle of the page behind the card.
        using var context = await ContextAsync();
        Action<Win2dCanvas, Font> Scene(bool clipped) => (canvas, font) =>
        {
            if (clipped)
            {
                using var path = canvas.GetGraphicsPath();
                // Rounded corners, so the clip is a real geometry (a rectangle is made an axis-aligned clip, which has no layer).
                const float r = 10;
                const float w = (float)Width, h = (float)Height;
                path.Start(r, 0);
                path.LineTo(w - r, 0);
                path.AddBezierTo(w, 0, w, 0, w, r);
                path.LineTo(w, h - r);
                path.AddBezierTo(w, h, w, h, w - r, h);
                path.LineTo(r, h);
                path.AddBezierTo(0, h, 0, h, 0, h - r);
                path.LineTo(0, r);
                path.AddBezierTo(0, 0, 0, 0, r, 0);
                path.CloseFigure();
                canvas.PushClip(path);
            }

            canvas.DrawRectangle(context.GetSolidBrush(PaintColor.FromArgb(255, 200, 40, 40)), 0, 0, Width, Height);
            canvas.DrawString(Sample, font, PaintColor.White, new PaintPoint(6, 6), new Core.Size(300, 20));
            canvas.DrawString(Sample, font, PaintColor.White, new PaintPoint(6, 32), new Core.Size(300, 20));
            if (clipped)
                canvas.PopClip();
        };

        // The same picture with no clip is the reference: the clip changes nothing about what is seen, so the two agree (up to ClearType against grayscale
        // fringes); the page behind the card showing through the words' rectangles is a large difference.
        var expected = Render(context, gpu: true, Scene(clipped: false));
        var actual = Render(context, gpu: true, Scene(clipped: true));

        Assert.True(Compare(expected, actual).Mean < 4, "text in a geometry clip was drawn over the wrong backdrop");
    }

    private static void RoundedCard(Win2dCanvas canvas)
    {
        using var path = canvas.GetGraphicsPath();
        const float r = 10;
        const float w = (float)Width - 20, h = (float)Height - 20;
        path.Start(10 + r, 10);
        path.LineTo(10 + w - r, 10);
        path.AddBezierTo(10 + w, 10, 10 + w, 10, 10 + w, 10 + r);
        path.LineTo(10 + w, 10 + h - r);
        path.AddBezierTo(10 + w, 10 + h, 10 + w, 10 + h, 10 + w - r, 10 + h);
        path.LineTo(10 + r, 10 + h);
        path.AddBezierTo(10, 10 + h, 10, 10 + h, 10, 10 + h - r);
        path.LineTo(10, 10 + r);
        path.AddBezierTo(10, 10, 10, 10, 10 + r, 10);
        path.CloseFigure();
        canvas.PushClip(path);
    }

    [Fact]
    public async Task TextInsideAGeometryClip_IsClearType_LikeTheSameTextWithNoClip()
    {
        using var context = await ContextAsync();
        Action<Win2dCanvas, Font> Scene(bool clipped) => (canvas, font) =>
        {
            if (clipped)
                RoundedCard(canvas);

            canvas.DrawRectangle(context.GetSolidBrush(PaintColor.FromArgb(255, 200, 40, 40)), 10, 10, Width - 20, Height - 20);
            canvas.DrawString(Sample, font, PaintColor.White, new PaintPoint(16, 14), new Core.Size(290, 20));
            canvas.DrawString(Sample, font, PaintColor.Black, new PaintPoint(16, 40), new Core.Size(290, 20));
            if (clipped)
                canvas.PopClip();
        };

        var expected = Render(context, gpu: true, Scene(clipped: false));
        var actual = Render(context, gpu: true, Scene(clipped: true));

        // Both are ClearType over the same fill, so they agree closely; grayscale text would differ in every fringe pixel.
        // The card's rounded corners differ from the square ones of the unclipped fill, so only the rows of text are compared.
        const int stride = (int)Width * 4;
        var rows = (14 * stride, 62 * stride);
        var (mean, _, within) = Compare(expected[rows.Item1..rows.Item2], actual[rows.Item1..rows.Item2]);
        Assert.True(mean < 0.3, $"mean difference {mean:F3}");
        Assert.True(within > 0.999, $"channels within 4 levels: {within:P3}");
        Assert.Equal(0, context.Counts.Total);
    }

    [Fact]
    public async Task TextInsideAGeometryClip_LeavesThePixelsOutsideTheGeometryAlone()
    {
        using var context = await ContextAsync();
        var untouched = Render(context, gpu: true, (canvas, font) => { });
        var actual = Render(context, gpu: true, (canvas, font) =>
        {
            RoundedCard(canvas);
            canvas.DrawRectangle(context.GetSolidBrush(PaintColor.FromArgb(255, 200, 40, 40)), 0, 0, Width, Height);
            canvas.DrawString(Sample, font, PaintColor.White, new PaintPoint(16, 14), new Core.Size(290, 20));
            canvas.PopClip();
        });

        // The corner pixel and the margin are outside the card.
        const int stride = (int)Width * 4;
        foreach (var (x, y) in new[] { (0, 0), (3, 3), ((int)Width - 1, (int)Height - 1), (5, (int)Height / 2) })
        {
            for (var c = 0; c < 4; c++)
                Assert.Equal(untouched[(y * stride) + (x * 4) + c], actual[(y * stride) + (x * 4) + c]);
        }
    }

    [Fact]
    public async Task NestedAndExcludingClips_ReleaseTheirSurfaces_EvenWhenLeftOpen()
    {
        using var context = await ContextAsync();
        Render(context, gpu: true, (canvas, font) =>
        {
            RoundedCard(canvas);
            canvas.PushClipExclude(new Rect(40, 30, 60, 20));
            canvas.PushTransform(Matrix3x2.CreateTranslation(2, 2));
            canvas.DrawString(Sample, font, PaintColor.Black, new PaintPoint(16, 14), new Core.Size(290, 20));
            canvas.PopTransform();
            canvas.PopClip();

            // The outer clip is left open: disposing the canvas releases it.
        });

        Assert.Equal(0, context.Counts.Total);
        Assert.Equal(0, context.Counts.ClipSurfaces);
    }

    [Fact]
    public async Task GpuLcdText_LooksLikeTheReadBackText()
    {
        using var context = await ContextAsync();
        var expected = Render(context, gpu: false, Words);
        var actual = Render(context, gpu: true, Words);

        var (mean, max, within) = Compare(expected, actual);
        Assert.True(mean < 0.3, $"mean difference {mean:F3}");
        Assert.True(within > 0.999, $"channels within 4 levels: {within:P3}");
        Assert.True(max <= 24, $"largest difference {max}");
    }

    [Fact]
    public async Task GpuLcdText_HasColourFringes_AndGrayscaleHasNone()
    {
        using var lcd = await ContextAsync(Win2dTextRendering.Lcd);
        using var gray = await ContextAsync(Win2dTextRendering.Grayscale);

        static int Fringed(byte[] bytes, int flatLeft, int flatRight)
        {
            // Over a gradient every pixel differs per channel; compare with what the same pixel is without text.
            return bytes.Length > 0 ? flatLeft + flatRight : 0;
        }

        var withText = Render(lcd, gpu: true, (canvas, font) => canvas.DrawString(Sample, font, PaintColor.Black, new PaintPoint(6, 6), new Core.Size(300, 20)));
        var without = Render(lcd, gpu: true, (_, _) => { });

        // Black text over a smooth gradient: with stripes the channels of an edge pixel are darkened by different amounts, grayscale by the same.
        static double Spread(byte[] text, byte[] background)
        {
            double total = 0;
            var count = 0;
            for (var i = 0; i < text.Length; i += 4)
            {
                var r = background[i + 2] == 0 ? 0 : (double)text[i + 2] / background[i + 2];
                var g = background[i + 1] == 0 ? 0 : (double)text[i + 1] / background[i + 1];
                var b = background[i] == 0 ? 0 : (double)text[i] / background[i];
                if (r < 0.98 || g < 0.98 || b < 0.98)
                {
                    total += Math.Max(r, Math.Max(g, b)) - Math.Min(r, Math.Min(g, b));
                    count++;
                }
            }

            return count == 0 ? 0 : total / count;
        }

        var grayText = Render(gray, gpu: true, (canvas, font) => canvas.DrawString(Sample, font, PaintColor.Black, new PaintPoint(6, 6), new Core.Size(300, 20)));
        Assert.True(Spread(withText, without) > 0.05, "LCD text should darken the channels of an edge pixel by different amounts");
        Assert.True(Spread(grayText, without) < 0.02, "grayscale text should darken them equally");
        _ = Fringed(withText, 0, 0);
    }

    [Fact]
    public async Task GrayscaleText_OnTheGpuPath_MatchesTheReadBackPath()
    {
        using var context = await ContextAsync(Win2dTextRendering.Grayscale);
        var (mean, max, within) = Compare(Render(context, false, Words), Render(context, true, Words));

        Assert.True(mean < 0.3, $"mean difference {mean:F3}");
        Assert.True(within > 0.999);
        Assert.True(max <= 24, $"largest difference {max}");
    }

    [Fact]
    public async Task ARectangleDrawnAfterText_CoversIt()
    {
        using var context = await ContextAsync();
        var bytes = Render(context, gpu: true, (canvas, font) =>
        {
            canvas.DrawString(Sample, font, PaintColor.Black, new PaintPoint(6, 6), new Core.Size(300, 20));
            canvas.DrawRectangle(new SolidBrush(PaintColor.FromArgb(255, 0, 200, 0)), 0, 0, 160, 40);
        });

        // Inside the rectangle every pixel is the rectangle's colour, wherever the text was; outside, the text is there.
        for (var y = 0; y < 40; y++)
        {
            for (var x = 0; x < 160; x++)
            {
                var o = ((y * (int)Width) + x) * 4;
                Assert.True(bytes[o] == 0 && bytes[o + 1] == 200 && bytes[o + 2] == 0, $"pixel {x},{y} is not the rectangle's colour");
            }
        }

        var without = Render(context, gpu: true, (canvas, _) => canvas.DrawRectangle(new SolidBrush(PaintColor.FromArgb(255, 0, 200, 0)), 0, 0, 160, 40));
        Assert.NotEqual(without, bytes);
    }

    [Fact]
    public async Task TextDrawnAfterARectangle_IsOnTopOfIt()
    {
        using var context = await ContextAsync();
        var expected = Render(context, gpu: false, (canvas, font) =>
        {
            canvas.DrawRectangle(new SolidBrush(PaintColor.FromArgb(255, 255, 240, 200)), 0, 0, 200, 40);
            canvas.DrawString(Sample, font, PaintColor.Black, new PaintPoint(6, 6), new Core.Size(300, 20));
        });
        var actual = Render(context, gpu: true, (canvas, font) =>
        {
            canvas.DrawRectangle(new SolidBrush(PaintColor.FromArgb(255, 255, 240, 200)), 0, 0, 200, 40);
            canvas.DrawString(Sample, font, PaintColor.Black, new PaintPoint(6, 6), new Core.Size(300, 20));
        });

        var (mean, _, within) = Compare(expected, actual);
        Assert.True(mean < 0.3 && within > 0.999, $"mean {mean:F3}, within 4 levels {within:P3}");
    }

    [Fact]
    public async Task TextUnderAClip_IsCutByIt()
    {
        using var context = await ContextAsync();
        var plain = Render(context, gpu: true, (_, _) => { });
        var clipped = Render(context, gpu: true, (canvas, font) =>
        {
            canvas.PushClip(new Rect(0, 0, 100, 40));
            canvas.DrawString(Sample, font, PaintColor.Black, new PaintPoint(6, 6), new Core.Size(300, 20));
            canvas.PopClip();
        });

        var ink = 0;
        for (var y = 0; y < (int)Height; y++)
        {
            for (var x = 0; x < (int)Width; x++)
            {
                var o = ((y * (int)Width) + x) * 4;
                var changed = clipped[o] != plain[o] || clipped[o + 1] != plain[o + 1] || clipped[o + 2] != plain[o + 2];
                if (changed)
                {
                    ink++;
                    Assert.True(x < 101 && y < 41, $"text leaked out of the clip at {x},{y}");
                }
            }
        }

        Assert.True(ink > 50, "text inside the clip should be drawn");
    }

    [Fact]
    public async Task TwoRunsOnTopOfEachOther_BlendWithEachOther()
    {
        using var context = await ContextAsync();
        void Scene(Win2dCanvas canvas, Font font)
        {
            canvas.DrawString(Sample, font, PaintColor.Black, new PaintPoint(6, 6), new Core.Size(300, 20));
            canvas.DrawString(Sample, font, PaintColor.Black, new PaintPoint(6, 6), new Core.Size(300, 20));
        }

        var (mean, max, within) = Compare(Render(context, false, Scene), Render(context, true, Scene));
        Assert.True(mean < 0.4 && within > 0.998, $"mean {mean:F3}, within 4 levels {within:P3}, max {max}");
    }

    [Fact]
    public async Task OnAHighDpiTarget_TheTextLandsOnTheSamePixels()
    {
        using var context = await ContextAsync();
        var expected = Render(context, gpu: false, Words, dpi: 144);
        var actual = Render(context, gpu: true, Words, dpi: 144);

        var (mean, max, within) = Compare(expected, actual);
        Assert.True(mean < 0.3 && within > 0.999, $"mean {mean:F3}, within 4 levels {within:P3}, max {max}");
    }

    [Fact]
    public async Task ADrawnAgainRun_ComesFromTheBitmapCache()
    {
        using var context = await ContextAsync();
        Render(context, gpu: true, Words);
        var bitmaps = context.TextBitmaps.Count;
        var misses = context.TextBitmaps.Misses;

        Render(context, gpu: true, Words);

        Assert.True(bitmaps > 0);
        Assert.Equal(misses, context.TextBitmaps.Misses);
        Assert.Equal(bitmaps, context.TextBitmaps.Count);
    }

    [Fact]
    public async Task EverythingTheGpuPathMakesIsReleased()
    {
        var context = await ContextAsync();
        Render(context, gpu: true, Words);

        Assert.Equal(0, context.Counts.Total);
        Assert.True(context.Counts.LcdLayers > 0);
        Assert.True(context.Counts.TextBitmaps > 0);

        var counts = context.Counts;
        context.Dispose();

        Assert.Equal(0, counts.LcdLayers);
        Assert.Equal(0, counts.TextBitmaps);
    }

    [Fact]
    public async Task ATargetWithAlpha_FallsBackToTheReadBackPath()
    {
        using var context = await ContextAsync();
        using var target = new CanvasRenderTarget(context.Device, (float)Width, (float)Height, 96, DirectXPixelFormat.B8G8R8A8UIntNormalized, CanvasAlphaMode.Premultiplied);
        using (var session = target.CreateDrawingSession())
        {
            session.Clear(Windows.UI.Color.FromArgb(255, 255, 255, 255));
            using var canvas = Win2dCanvas.FromRenderTarget(context, target, session, new Rect(0, 0, Width, Height));
            canvas.DrawString(Sample, context.GetFont("Source Sans 3", 16, PaintFontStyle.Regular)!, PaintColor.Black, new PaintPoint(6, 6), new Core.Size(300, 20));
        }

        Assert.Equal(0, context.Counts.LcdLayers);
        Assert.Contains(target.GetPixelBytes(), b => b < 200);
    }
}
