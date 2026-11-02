using Microsoft.Graphics.Canvas;
using PeachDrawing.Core;
using PeachDrawing.Win2D;
using PeachPDF.Tests.TestSupport;
using Windows.Graphics.DirectX;

namespace PeachDrawing.Win2D.Tests;

/// <summary>
/// Text over a drawing session the host owns (a control's, a render target's): what <see cref="Win2dTextRendering.Auto"/> picks there, and LCD
/// text blended against pixels read back through the session's native target.
/// </summary>
public class HostSessionTextTests
{
    private const double Width = 200;
    private const double Height = 60;

    private static async Task<Win2dRenderContext> ContextAsync(Win2dTextRendering mode)
    {
        var context = Win2dRenderContext.CreateSoftware();
        context.TextRendering = mode;
        await using var stream = File.OpenRead(BundledFonts.Ttf);
        await context.AddFont(stream, "Source Sans 3");
        return context;
    }

    private static CanvasRenderTarget Target(Win2dRenderContext context, CanvasAlphaMode alpha, float dpi = 96) =>
        new(context.Device, (float)Width, (float)Height, dpi, DirectXPixelFormat.B8G8R8A8UIntNormalized, alpha);

    private static void Paint(Win2dRenderContext context, CanvasRenderTarget target, Action<Win2dCanvas>? before, double size, string text, double x = 5, double y = 5)
    {
        using var session = target.CreateDrawingSession();
        session.Clear(Windows.UI.Color.FromArgb(255, 255, 255, 255));
        using var canvas = Win2dCanvas.FromSession(context, session, new Rect(0, 0, Width, Height));
        before?.Invoke(canvas);
        var font = context.GetFont("Source Sans 3", size, PaintFontStyle.Regular)!;
        canvas.DrawString(text, font, PaintColor.Black, new PaintPoint(x, y), canvas.MeasureString(text, font));
    }

    private static int Fringed(CanvasRenderTarget target)
    {
        var bytes = target.GetPixelBytes();
        var count = 0;
        for (var i = 0; i < bytes.Length; i += 4)
        {
            if (bytes[i] != bytes[i + 1] || bytes[i + 1] != bytes[i + 2])
                count++;
        }

        return count;
    }

    private static int Ink(CanvasRenderTarget target)
    {
        var bytes = target.GetPixelBytes();
        var count = 0;
        for (var i = 0; i < bytes.Length; i += 4)
        {
            if (bytes[i + 1] < 250)
                count++;
        }

        return count;
    }

    private static int LeftmostInk(CanvasRenderTarget target)
    {
        var bytes = target.GetPixelBytes();
        var width = (int)target.SizeInPixels.Width;
        var height = (int)target.SizeInPixels.Height;
        for (var x = 0; x < width; x++)
        {
            for (var y = 0; y < height; y++)
            {
                if (bytes[((y * width) + x) * 4 + 1] < 200)
                    return x;
            }
        }

        return -1;
    }

    [Fact]
    public async Task The_native_reader_sees_what_was_just_drawn()
    {
        using var context = await ContextAsync(Win2dTextRendering.Outline);
        using var target = Target(context, CanvasAlphaMode.Ignore);
        using var session = target.CreateDrawingSession();
        session.Clear(Windows.UI.Color.FromArgb(255, 200, 30, 30));
        session.FillRectangle(10, 5, 20, 10, Windows.UI.Color.FromArgb(255, 30, 30, 200));

        using var reader = Win2dNativeTarget.TryCreate(session, context.Counts)!;
        Assert.NotNull(reader);
        Assert.True(reader.IsOpaque);
        Assert.Equal((int)Width, reader.Width);
        Assert.Equal((0.0, 0.0), reader.Origin());

        var pixels = new byte[4 * 4 * 4];
        Assert.True(reader.TryRead(12, 7, 4, 4, pixels));
        Assert.Equal((30, 30, 200), (pixels[2], pixels[1], pixels[0]));
        Assert.True(reader.TryRead(0, 0, 4, 4, pixels));
        Assert.Equal((200, 30, 30), (pixels[2], pixels[1], pixels[0]));
    }

    [Fact]
    public async Task The_native_reader_matches_a_direct_read_of_the_bitmap()
    {
        using var context = await ContextAsync(Win2dTextRendering.Outline);
        using var target = Target(context, CanvasAlphaMode.Ignore);
        using var session = target.CreateDrawingSession();
        session.Clear(Windows.UI.Color.FromArgb(255, 10, 20, 30));
        session.FillRectangle(40, 20, 30, 10, Windows.UI.Color.FromArgb(255, 200, 100, 50));
        session.FillRectangle(0, 0, 5, 5, Windows.UI.Color.FromArgb(255, 1, 2, 3));

        using var reader = Win2dNativeTarget.TryCreate(session, context.Counts)!;
        var read = new byte[50 * 30 * 4];
        Assert.True(reader.TryRead(30, 15, 50, 30, read));

        session.Flush();
        Assert.Equal(target.GetPixelBytes(30, 15, 50, 30), read);

        // And again for a different, smaller rectangle: the staging bitmap is reused.
        var again = new byte[8 * 8 * 4];
        Assert.True(reader.TryRead(0, 0, 8, 8, again));
        Assert.Equal(target.GetPixelBytes(0, 0, 8, 8), again);
    }

    [Fact]
    public async Task The_native_reader_refuses_a_rectangle_outside_the_target()
    {
        using var context = await ContextAsync(Win2dTextRendering.Outline);
        using var target = Target(context, CanvasAlphaMode.Ignore);
        using var session = target.CreateDrawingSession();
        using var reader = Win2dNativeTarget.TryCreate(session, context.Counts)!;

        var pixels = new byte[400 * 400 * 4];
        Assert.False(reader.TryRead(-1, 0, 4, 4, pixels));
        Assert.False(reader.TryRead((int)Width - 2, 0, 4, 4, pixels));
        Assert.False(reader.TryRead(0, 0, 0, 4, pixels));
        Assert.False(reader.TryRead(0, 0, 400, 400, pixels));
        Assert.False(reader.TryRead(0, 0, 4, 4, new byte[4]));
    }

    [Fact]
    public async Task A_session_that_records_into_a_command_list_has_no_native_target()
    {
        using var context = await ContextAsync(Win2dTextRendering.Outline);
        using var list = new CanvasCommandList(context.Device);
        using var recording = list.CreateDrawingSession();

        Assert.Null(Win2dNativeTarget.TryCreate(recording, context.Counts));
        Assert.Equal(0, context.Counts.NativeTargets);
    }

    [Fact]
    public async Task A_target_with_an_alpha_channel_is_not_opaque()
    {
        using var context = await ContextAsync(Win2dTextRendering.Outline);
        using var target = Target(context, CanvasAlphaMode.Premultiplied);
        using var session = target.CreateDrawingSession();
        using var reader = Win2dNativeTarget.TryCreate(session, context.Counts)!;

        Assert.False(reader.IsOpaque);
    }

    [Fact]
    public async Task Lcd_text_over_a_hosts_session_has_colour_fringes_and_leaves_no_native_resource_behind()
    {
        using var context = await ContextAsync(Win2dTextRendering.Lcd);
        using var target = Target(context, CanvasAlphaMode.Ignore);
        Paint(context, target, null, 16, "Hamburgefonstiv");

        Assert.True(Fringed(target) > 20, Fringed(target).ToString());
        Assert.Equal(0, context.Counts.Total);
    }

    [Fact]
    public async Task Lcd_text_over_a_hosts_session_is_blended_against_what_was_drawn_before_it()
    {
        using var context = await ContextAsync(Win2dTextRendering.Lcd);
        using var target = Target(context, CanvasAlphaMode.Ignore);
        Paint(context, target, canvas => canvas.DrawRectangle(context.GetSolidBrush(PaintColor.FromArgb(255, 255, 0, 0)), 0, 0, Width, Height), 16, "Hamburgefonstiv");

        // Black text over red can only darken red: green and blue are zero before and after, so no blending against white was done.
        var bytes = target.GetPixelBytes();
        var darkenedRed = false;
        for (var i = 0; i < bytes.Length; i += 4)
        {
            Assert.Equal(0, bytes[i]);
            Assert.Equal(0, bytes[i + 1]);
            darkenedRed |= bytes[i + 2] < 100;
        }

        Assert.True(darkenedRed);
    }

    [Theory]
    [InlineData(96f)]
    [InlineData(144f)]
    [InlineData(192f)]
    public async Task Lcd_text_over_a_hosts_session_with_a_transform_and_a_dpi_lands_where_grayscale_text_does(float dpi)
    {
        using var lcd = await ContextAsync(Win2dTextRendering.Lcd);
        using var gray = await ContextAsync(Win2dTextRendering.Grayscale);
        using var lcdTarget = Target(lcd, CanvasAlphaMode.Ignore, dpi);
        using var grayTarget = Target(gray, CanvasAlphaMode.Ignore, dpi);
        Paint(lcd, lcdTarget, c => c.PushTransform(System.Numerics.Matrix3x2.CreateTranslation(11.5f, 3.25f)), 14, "Hamburgefonstiv");
        Paint(gray, grayTarget, c => c.PushTransform(System.Numerics.Matrix3x2.CreateTranslation(11.5f, 3.25f)), 14, "Hamburgefonstiv");

        Assert.True(Fringed(lcdTarget) > 20, $"dpi {dpi}");
        Assert.InRange(Ink(lcdTarget), Ink(grayTarget) * 0.85, Ink(grayTarget) * 1.25);
        Assert.InRange(LeftmostInk(lcdTarget), LeftmostInk(grayTarget) - 2, LeftmostInk(grayTarget) + 2);
    }

    [Fact]
    public async Task Lcd_text_over_a_target_with_alpha_is_drawn_as_grayscale()
    {
        using var context = await ContextAsync(Win2dTextRendering.Lcd);
        using var target = Target(context, CanvasAlphaMode.Premultiplied);
        Paint(context, target, null, 16, "Hamburgefonstiv");

        Assert.Equal(0, Fringed(target));
        Assert.True(Ink(target) > 50);
        Assert.Equal(0, context.Counts.Total);
    }

    [Fact]
    public async Task Lcd_text_inside_a_clip_over_a_hosts_session_still_blends_against_what_was_drawn_in_the_clip()
    {
        using var context = await ContextAsync(Win2dTextRendering.Lcd);
        using var target = Target(context, CanvasAlphaMode.Ignore);
        Paint(context, target, canvas =>
        {
            canvas.PushClip(new Rect(0, 0, Width, Height));
            canvas.DrawRectangle(context.GetSolidBrush(PaintColor.FromArgb(255, 255, 0, 0)), 0, 0, Width, Height);
        }, 16, "Hamburgefonstiv");

        // The red was drawn inside the clip, and the black text over it never lets green or blue back in.
        var bytes = target.GetPixelBytes();
        for (var i = 0; i < bytes.Length; i += 4)
        {
            Assert.Equal(0, bytes[i]);
            Assert.Equal(0, bytes[i + 1]);
        }

        Assert.Contains(Enumerable.Range(0, bytes.Length / 4), p => bytes[p * 4 + 2] < 100);
    }

    [Fact]
    public async Task Lcd_text_with_a_non_normal_blend_is_grayscale()
    {
        using var context = await ContextAsync(Win2dTextRendering.Lcd);
        using var target = Target(context, CanvasAlphaMode.Ignore);
        Paint(context, target, c => c.PushBlendMode(PaintBlendMode.Plus), 16, "Hamburgefonstiv");

        Assert.Equal(0, Fringed(target));
    }

    [Fact]
    public async Task Auto_gives_a_composer_grayscale_text_and_a_hosts_session_what_clear_type_says()
    {
        using var context = await ContextAsync(Win2dTextRendering.Auto);

        using (var composer = new Win2dComposer(context, Width, Height, new Win2dComposerOptions { Background = PaintColor.White }))
        {
            var font = context.GetFont("Source Sans 3", 16, PaintFontStyle.Regular)!;
            composer.Canvas.DrawString("Hamburgefonstiv", font, PaintColor.Black, new PaintPoint(5, 5), new Core.Size(150, 20));
            var buffer = composer.ToPixelBuffer().PremultipliedRgba.ToArray();
            for (var i = 0; i < buffer.Length; i += 4)
                Assert.True(buffer[i] == buffer[i + 1] && buffer[i + 1] == buffer[i + 2], "an export must have no colour fringes");
        }

        using var target = Target(context, CanvasAlphaMode.Ignore);
        Paint(context, target, null, 16, "Hamburgefonstiv");
        if (Win2dSystemText.ClearTypeEnabled)
            Assert.True(Fringed(target) > 20);
        else
            Assert.Equal(0, Fringed(target));
    }

    [Fact]
    public async Task Auto_draws_large_text_as_an_outline_and_body_text_as_a_mask()
    {
        using var auto = await ContextAsync(Win2dTextRendering.Auto);
        using var outline = await ContextAsync(Win2dTextRendering.Outline);

        byte[] Render(Win2dRenderContext context, double size)
        {
            using var composer = new Win2dComposer(context, 300, 120, new Win2dComposerOptions { Background = PaintColor.White });
            var font = context.GetFont("Source Sans 3", size, PaintFontStyle.Regular)!;
            composer.Canvas.DrawString("Hag", font, PaintColor.Black, new PaintPoint(5, 5), new Core.Size(200, 100));
            return composer.ToPixelBuffer().PremultipliedRgba.ToArray();
        }

        Assert.Equal(Render(outline, 72), Render(auto, 72));
        Assert.NotEqual(Render(outline, 12), Render(auto, 12));
    }

    [Fact]
    public async Task Auto_does_not_open_the_native_target_for_text_that_cannot_use_it()
    {
        // A large run is an outline, so the session is never asked for its native target.
        using var context = await ContextAsync(Win2dTextRendering.Auto);
        using var target = Target(context, CanvasAlphaMode.Ignore);
        Paint(context, target, null, 100, "Hg");

        Assert.Equal(0, context.Counts.Total);
        Assert.True(Ink(target) > 50);
    }
}
