using PeachDrawing.Core;
using PeachDrawing.Win2D;
using PeachPDF.Tests.TestSupport;
using System.Numerics;

namespace PeachDrawing.Win2D.Tests;

/// <summary>
/// <see cref="Win2dTextRendering"/>: text drawn as hinted, weight-corrected coverage placed on whole device pixels (and, for LCD, with a coverage
/// per stripe), read back from a software-rendered page.
/// </summary>
public class TextRenderingTests
{
    private const string Sample = "Hamburgefonstiv";

    private static async Task<Scene> SceneAsync(Win2dTextRendering mode, PaintColor? background = null, float dpi = 96, double width = 200, double height = 60)
    {
        var scene = Scene.Create(width, height, options: new Win2dComposerOptions { Background = background ?? PaintColor.Transparent, Dpi = dpi });
        scene.Context.TextRendering = mode;
        await using var stream = File.OpenRead(BundledFonts.Ttf);
        await scene.Context.AddFont(stream, "Source Sans 3");
        return scene;
    }

    private static void Draw(Scene scene, PaintColor color, string text = Sample, double size = 16, double x = 5, double y = 5)
    {
        var font = scene.Context.GetFont("Source Sans 3", size, PaintFontStyle.Regular)!;
        scene.Canvas.DrawString(text, font, color, new PaintPoint(x, y), scene.Canvas.MeasureString(text, font));
    }

    /// <summary>The device-pixel bounds of everything not the background, or null when nothing is drawn.</summary>
    private static (int Left, int Top, int Right, int Bottom)? Bounds(Scene scene, Func<(int R, int G, int B, int A), bool> isInk)
    {
        var width = (int)scene.Composer.PixelWidth;
        var height = (int)scene.Composer.PixelHeight;
        var (left, top, right, bottom) = (int.MaxValue, int.MaxValue, -1, -1);
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            if (!isInk(scene.Px(x, y)))
                continue;

            left = Math.Min(left, x);
            right = Math.Max(right, x);
            top = Math.Min(top, y);
            bottom = Math.Max(bottom, y);
        }

        return right < 0 ? null : (left, top, right, bottom);
    }

    private static long Ink(Scene scene, Func<(int R, int G, int B, int A), int> weight)
    {
        long total = 0;
        for (var y = 0; y < scene.Composer.PixelHeight; y++)
        for (var x = 0; x < scene.Composer.PixelWidth; x++)
            total += weight(scene.Px(x, y));

        return total;
    }

    private static bool Identical(Scene a, Scene b)
    {
        for (var y = 0; y < a.Composer.PixelHeight; y++)
        for (var x = 0; x < a.Composer.PixelWidth; x++)
        {
            if (a.Px(x, y) != b.Px(x, y))
                return false;
        }

        return true;
    }

    [Fact]
    public async Task Grayscale_text_is_inked_where_outline_text_is()
    {
        using var outline = await SceneAsync(Win2dTextRendering.Outline);
        using var grayscale = await SceneAsync(Win2dTextRendering.Grayscale);
        Draw(outline, PaintColor.Black);
        Draw(grayscale, PaintColor.Black);

        var expected = Bounds(outline, p => p.A > 0)!.Value;
        var actual = Bounds(grayscale, p => p.A > 0)!.Value;

        Assert.InRange(actual.Left, expected.Left - 2, expected.Left + 2);
        Assert.InRange(actual.Right, expected.Right - 2, expected.Right + 2);
        Assert.InRange(actual.Top, expected.Top - 2, expected.Top + 2);
        Assert.InRange(actual.Bottom, expected.Bottom - 2, expected.Bottom + 2);
        Assert.False(Identical(outline, grayscale), "hinting and the weight correction should change the pixels");
    }

    [Fact]
    public async Task Hinted_text_sits_on_a_whole_pixel_row_where_outline_text_straddles_two()
    {
        // The baseline of text placed at a half-pixel is half a pixel from a row edge: filled as an outline the bottom row of an E is half covered,
        // hinted its stems are fitted so the bottom of the letter lands on a row edge and that row is solid.
        using var outline = await SceneAsync(Win2dTextRendering.Outline);
        using var grayscale = await SceneAsync(Win2dTextRendering.Grayscale);
        Draw(outline, PaintColor.Black, "E", size: 24, y: 5.5);
        Draw(grayscale, PaintColor.Black, "E", size: 24, y: 5.5);

        int StrongestInLastInkRow(Scene scene)
        {
            var bounds = Bounds(scene, p => p.A > 0)!.Value;
            return Enumerable.Range(bounds.Left, bounds.Right - bounds.Left + 1).Max(x => scene.Px(x, bounds.Bottom).A);
        }

        Assert.True(StrongestInLastInkRow(grayscale) >= 200, StrongestInLastInkRow(grayscale).ToString());
        Assert.True(StrongestInLastInkRow(outline) < 200, StrongestInLastInkRow(outline).ToString());
    }

    [Fact]
    public async Task Light_text_on_a_dark_page_gets_heavier_and_dark_text_does_not()
    {
        // The eye does not add up brightness the way blending does: light text blended as it is looks too thin.
        using var lightOutline = await SceneAsync(Win2dTextRendering.Outline, PaintColor.Black);
        using var lightGrayscale = await SceneAsync(Win2dTextRendering.Grayscale, PaintColor.Black);
        Draw(lightOutline, PaintColor.White, size: 12);
        Draw(lightGrayscale, PaintColor.White, size: 12);
        Assert.True(Ink(lightGrayscale, p => p.R) > Ink(lightOutline, p => p.R) * 1.05);

        using var darkOutline = await SceneAsync(Win2dTextRendering.Outline, PaintColor.White);
        using var darkGrayscale = await SceneAsync(Win2dTextRendering.Grayscale, PaintColor.White);
        Draw(darkOutline, PaintColor.Black, size: 12);
        Draw(darkGrayscale, PaintColor.Black, size: 12);
        Assert.True(Ink(darkGrayscale, p => 255 - p.R) < Ink(darkOutline, p => 255 - p.R) * 1.05);
    }

    [Theory]
    [InlineData(0, 0, 0, false)]
    [InlineData(255, 255, 255, false)]
    [InlineData(128, 128, 128, false)]
    [InlineData(200, 40, 40, false)]
    [InlineData(0, 0, 0, true)]
    [InlineData(255, 255, 255, true)]
    public void The_weight_correction_keeps_the_ends_and_never_reverses_the_order_of_coverage(int r, int g, int b, bool lcd)
    {
        var table = Win2dTextBlend.CorrectionTable(PaintColor.FromArgb(255, r, g, b), lcd);

        Assert.Equal(0, table[0]);
        Assert.Equal(255, table[255]);
        for (var i = 1; i < 256; i++)
            Assert.True(table[i] >= table[i - 1], $"coverage {i}");
    }

    [Fact]
    public void The_weight_correction_lightens_dark_text_and_darkens_light_text_at_half_coverage()
    {
        Assert.True(Win2dTextBlend.CorrectionTable(PaintColor.Black)[128] < 128);
        Assert.True(Win2dTextBlend.CorrectionTable(PaintColor.White)[128] > 128);
    }

    [Fact]
    public async Task Text_under_a_rotation_has_no_pixel_grid_to_fit_to_and_is_drawn_as_an_outline()
    {
        using var outline = await SceneAsync(Win2dTextRendering.Outline);
        using var grayscale = await SceneAsync(Win2dTextRendering.Grayscale);
        foreach (var scene in new[] { outline, grayscale })
        {
            scene.Canvas.PushTransform(Matrix3x2.CreateRotation(0.4f) * Matrix3x2.CreateTranslation(30, 5));
            Draw(scene, PaintColor.Black, x: 0, y: 0);
            scene.Canvas.PopTransform();
        }

        Assert.NotNull(Bounds(grayscale, p => p.A > 0));
        Assert.True(Identical(outline, grayscale));
    }

    [Fact]
    public async Task A_colour_font_is_drawn_as_an_outline_run()
    {
        var scenes = new List<Scene>();
        foreach (var mode in new[] { Win2dTextRendering.Outline, Win2dTextRendering.Grayscale })
        {
            var scene = Scene.Create(80, 40, options: new Win2dComposerOptions { Background = PaintColor.White });
            scene.Context.TextRendering = mode;
            await using (var stream = File.OpenRead(BundledFonts.ColorV0))
                await scene.Context.AddFont(stream, "Color Test");

            var font = scene.Context.GetFont("Color Test", 30, PaintFontStyle.Regular)!;
            scene.Canvas.DrawString("A", font, PaintColor.Black, new PaintPoint(5, 5), new Core.Size(30, 30));
            scenes.Add(scene);
        }

        try
        {
            Assert.True(Identical(scenes[0], scenes[1]));
            Assert.True(scenes[1].OpaquePixels() > 0);
        }
        finally
        {
            scenes.ForEach(s => s.Dispose());
        }
    }

    [Theory]
    [InlineData(96f)]
    [InlineData(144f)]
    [InlineData(192f)]
    public async Task Text_lands_where_outline_text_does_at_any_dpi(float dpi)
    {
        using var outline = await SceneAsync(Win2dTextRendering.Outline, dpi: dpi);
        using var grayscale = await SceneAsync(Win2dTextRendering.Grayscale, dpi: dpi);
        Draw(outline, PaintColor.Black, x: 7.5, y: 6.25);
        Draw(grayscale, PaintColor.Black, x: 7.5, y: 6.25);

        var expected = Bounds(outline, p => p.A > 0)!.Value;
        var actual = Bounds(grayscale, p => p.A > 0)!.Value;
        Assert.InRange(actual.Left, expected.Left - 2, expected.Left + 2);
        Assert.InRange(actual.Right, expected.Right - 2, expected.Right + 2);
        Assert.InRange(actual.Top, expected.Top - 2, expected.Top + 2);
        Assert.InRange(actual.Bottom, expected.Bottom - 2, expected.Bottom + 2);
    }

    [Fact]
    public async Task Translucent_text_scales_the_corrected_coverage()
    {
        using var scene = await SceneAsync(Win2dTextRendering.Grayscale, PaintColor.White);
        Draw(scene, PaintColor.FromArgb(128, 0, 0, 0), "I", size: 30);

        var darkest = Enumerable.Range(0, (int)scene.Composer.PixelWidth).Max(x => Enumerable.Range(0, (int)scene.Composer.PixelHeight).Max(y => 255 - scene.Px(x, y).R));
        Assert.InRange(darkest, 100, 135);
    }

    [Fact]
    public async Task Huge_text_on_a_page_is_cropped_to_it_and_drawn_hinted()
    {
        using var scene = await SceneAsync(Win2dTextRendering.Grayscale, PaintColor.White, width: 40, height: 40);
        Draw(scene, PaintColor.Black, "I", size: 20000, x: -2500, y: -19900);

        Assert.True(scene.Px(20, 20).R < 128, scene.Px(20, 20).ToString());
    }

    [Fact]
    public async Task A_run_too_large_to_rasterize_on_a_session_of_the_hosts_is_drawn_as_an_outline()
    {
        // Over a session the host owns there is no page to crop to, so a run that is thousands of pixels across is not rasterized at all.
        using var context = Win2dRenderContext.CreateSoftware();
        context.TextRendering = Win2dTextRendering.Grayscale;
        await using (var stream = File.OpenRead(BundledFonts.Ttf))
            await context.AddFont(stream, "Source Sans 3");

        using var target = new Microsoft.Graphics.Canvas.CanvasRenderTarget(context.Device, 40, 40, 96f);
        using (var session = target.CreateDrawingSession())
        {
            session.Clear(Windows.UI.Color.FromArgb(255, 255, 255, 255));
            using var canvas = Win2dCanvas.FromSession(context, session, new Rect(0, 0, 40, 40));
            var font = context.GetFont("Source Sans 3", 20000, PaintFontStyle.Regular)!;
            canvas.DrawString("I", font, PaintColor.Black, new PaintPoint(-2500, -19900), new Core.Size(100, 100));
        }

        var bytes = target.GetPixelBytes();
        Assert.True(bytes[(20 * 40 + 20) * 4] < 128, bytes[(20 * 40 + 20) * 4].ToString());
    }

    [Fact]
    public async Task The_render_context_gives_back_every_native_resource_after_text_in_each_mode()
    {
        foreach (var mode in new[] { Win2dTextRendering.Grayscale, Win2dTextRendering.Lcd })
        {
            using var scene = await SceneAsync(mode, PaintColor.White);
            Draw(scene, PaintColor.Black);
            scene.Canvas.PushClip(new Rect(0, 0, 50, 50));
            Draw(scene, PaintColor.Black, y: 30);
            scene.Canvas.PopClip();
            scene.Canvas.Dispose();

            Assert.Equal(0, scene.Context.Counts.Total);
        }
    }

    // ---- LCD ---------------------------------------------------------------------------------------------------

    private static bool Fringed((int R, int G, int B, int A) p) => p.A == 255 && (p.R != p.G || p.G != p.B);

    private static int CountFringed(Scene scene)
    {
        var count = 0;
        for (var y = 0; y < scene.Composer.PixelHeight; y++)
        for (var x = 0; x < scene.Composer.PixelWidth; x++)
        {
            if (Fringed(scene.Px(x, y)))
                count++;
        }

        return count;
    }

    [Fact]
    public async Task Lcd_text_has_colour_fringes_where_grayscale_text_has_none()
    {
        using var grayscale = await SceneAsync(Win2dTextRendering.Grayscale, PaintColor.White);
        using var lcd = await SceneAsync(Win2dTextRendering.Lcd, PaintColor.White);
        Draw(grayscale, PaintColor.Black);
        Draw(lcd, PaintColor.Black);

        Assert.Equal(0, CountFringed(grayscale));
        Assert.True(CountFringed(lcd) > 20, CountFringed(lcd).ToString());
        Assert.NotNull(Bounds(lcd, p => p.R < 250 || p.G < 250 || p.B < 250));
    }

    [Fact]
    public async Task Lcd_text_is_blended_against_the_pixels_behind_it_channel_by_channel()
    {
        using var scene = await SceneAsync(Win2dTextRendering.Lcd, PaintColor.White);
        scene.Canvas.DrawRectangle(scene.Solid(255, 0, 0), 0, 0, 200, 60);
        Draw(scene, PaintColor.Black);

        // What the text did not reach is still the red behind it, and the page is still opaque everywhere.
        Assert.Equal((255, 0, 0, 255), scene.Px(199, 59));
        Assert.Equal((255, 0, 0, 255), scene.Px(0, 0));
        Assert.All(Enumerable.Range(0, 200), x => Assert.Equal(255, scene.Px(x, 20).A));

        // Black text over red can only darken the red channel; green and blue were already zero.
        var darkenedRed = false;
        for (var y = 0; y < 60; y++)
        for (var x = 0; x < 200; x++)
        {
            var p = scene.Px(x, y);
            Assert.Equal(0, p.G);
            Assert.Equal(0, p.B);
            darkenedRed |= p.R < 100;
        }

        Assert.True(darkenedRed);
    }

    [Fact]
    public async Task Lcd_text_in_the_opposite_stripe_order_mirrors_the_fringes()
    {
        long Balance(bool bgr)
        {
            using var scene = SceneAsyncSync(bgr);
            Draw(scene, PaintColor.Black);
            long total = 0;
            for (var y = 0; y < scene.Composer.PixelHeight; y++)
            for (var x = 0; x < scene.Composer.PixelWidth; x++)
                total += scene.Px(x, y).R - scene.Px(x, y).B;

            return total;
        }

        Scene SceneAsyncSync(bool bgr)
        {
            var scene = SceneAsync(Win2dTextRendering.Lcd, PaintColor.White).GetAwaiter().GetResult();
            scene.Context.LcdBgrStripes = bgr;
            return scene;
        }

        var rgb = Balance(false);
        var reversed = Balance(true);

        // A left edge of a stem is darker in the blue stripe of an RGB pixel (its right side) than in the red; the opposite order flips that.
        Assert.NotEqual(0, rgb);
        Assert.True(Math.Sign(rgb) == -Math.Sign(reversed), $"{rgb} vs {reversed}");
    }

    [Fact]
    public async Task Lcd_text_inside_a_clip_keeps_its_fringes_and_the_pixels_behind_it()
    {
        // What is drawn inside a clip is in the bitmap once it is flushed, so the text is blended against it as it is outside one.
        using var scene = await SceneAsync(Win2dTextRendering.Lcd, PaintColor.White);
        scene.Canvas.PushClip(new Rect(0, 0, 200, 60));
        scene.Canvas.DrawRectangle(scene.Solid(255, 0, 0), 0, 0, 200, 60);
        Draw(scene, PaintColor.Black);
        scene.Canvas.PopClip();

        Assert.Equal((255, 0, 0, 255), scene.Px(199, 59));
        for (var y = 0; y < 60; y++)
        for (var x = 0; x < 200; x++)
        {
            var p = scene.Px(x, y);
            Assert.Equal(0, p.G);
            Assert.Equal(0, p.B);
        }
    }

    [Fact]
    public async Task Lcd_text_over_a_page_with_transparency_is_drawn_as_grayscale()
    {
        using var scene = await SceneAsync(Win2dTextRendering.Lcd);
        Draw(scene, PaintColor.FromArgb(255, 200, 0, 0));

        // An LCD patch is opaque; a page that is still clear around the text proves none was made.
        Assert.Equal(0, scene.Px(1, 1).A);
        Assert.True(scene.OpaquePixels() > 50);
        Assert.Equal(0, Ink(scene, p => p.A == 255 && p.R < 50 ? 1 : 0));
    }

    [Fact]
    public async Task Lcd_text_that_runs_off_the_page_is_cropped_to_it_and_keeps_its_fringes()
    {
        using var scene = await SceneAsync(Win2dTextRendering.Lcd, PaintColor.White);
        Draw(scene, PaintColor.Black, x: -30);
        Draw(scene, PaintColor.Black, x: 150, y: 40);
        Draw(scene, PaintColor.Black, x: 5, y: -8);
        Draw(scene, PaintColor.Black, x: 500, y: 500);

        Assert.NotNull(Bounds(scene, p => p.R < 250));
        Assert.True(CountFringed(scene) > 20);
    }

    [Fact]
    public async Task Text_after_lcd_text_still_sees_the_drawing_before_it()
    {
        // Reading the bitmap needs the pending drawing flushed into it first; text drawn over a shape just drawn must show the shape.
        using var scene = await SceneAsync(Win2dTextRendering.Lcd, PaintColor.White);
        scene.Canvas.DrawRectangle(scene.Solid(0, 0, 255), 0, 0, 200, 30);
        Draw(scene, PaintColor.White, y: 5);

        Assert.Equal((0, 0, 255, 255), scene.Px(190, 10));
        var inkedWhite = false;
        for (var x = 0; x < 120 && !inkedWhite; x++)
            inkedWhite = scene.Px(x, 12).R > 200;

        Assert.True(inkedWhite);
    }
}
