using PeachDrawing.Core;
using PeachDrawing.Skia;
using PeachPDF.Tests.TestSupport;
using SkiaSharp;
using System.Numerics;

namespace PeachDrawing.Skia.Tests;

/// <summary>
/// Text drawn as a hinted mask (<see cref="SkiaTextRendering"/>) on a CPU surface the test supplies. The mask cache says which path a run took
/// (a run drawn as an outline never makes an entry), and the pixels say what the path did.
/// </summary>
public class TextRenderingTests
{
    private sealed class FakeSystem(bool subpixel, bool bgr) : ISystemTextSettings
    {
        public SystemTextSettings Get() => new(subpixel, bgr);
    }

    private sealed class Surface : IDisposable
    {
        private readonly SKSurface _surface;
        private readonly SKBitmap _bitmap;

        public Surface(SkiaRenderContext context, int width, int height, SKColor background, double scale = 1)
        {
            Context = context;
            Width = width;
            Height = height;
            _bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
            _surface = SKSurface.Create(_bitmap.Info, _bitmap.GetPixels(), _bitmap.RowBytes)!;
            _surface.Canvas.Clear(background);
            _surface.Canvas.Scale((float)scale);
        }

        public SkiaRenderContext Context { get; }

        public int Width { get; }

        public int Height { get; }

        public SKCanvas Raw => _surface.Canvas;

        public SkiaCanvas Canvas(bool opaque = true)
        {
            var canvas = SkiaCanvas.FromCanvas(Context, _surface.Canvas, new Rect(0, 0, Width, Height));
            canvas.OpaqueBackdrop = opaque;
            return canvas;
        }

        /// <summary>The pixel at (x, y) as R, G, B, A.</summary>
        public (int R, int G, int B, int A) Px(int x, int y)
        {
            var c = _bitmap.GetPixel(x, y);
            return (c.Red, c.Green, c.Blue, c.Alpha);
        }

        public void Dispose()
        {
            _surface.Dispose();
            _bitmap.Dispose();
        }
    }

    private static async Task<SkiaRenderContext> ContextAsync(SkiaTextRendering mode, bool subpixel = false, bool bgr = false)
    {
        var context = new SkiaRenderContext(SkiaTextEngine.PeachDrawing, SkiaImageCodec.PeachImage)
        {
            TextRendering = mode,
            SystemText = new FakeSystem(subpixel, bgr),
        };
        await using var stream = File.OpenRead(BundledFonts.Ttf);
        await context.AddFont(stream, "Source Sans 3");
        return context;
    }

    private static readonly SKColor White = new(255, 255, 255);

    private static void Draw(Canvas canvas, SkiaRenderContext context, string text, PaintColor color, double size = 14, double x = 4, double y = 4)
    {
        var font = context.GetFont("Source Sans 3", size, PaintFontStyle.Regular)!;
        canvas.DrawString(text, font, color, new PaintPoint(x, y), canvas.MeasureString(text, font));
    }

    // The strongest disagreement between two color channels of one pixel, over every pixel: 0 for neutral (gray) text.
    private static int Fringe(Surface s)
    {
        var most = 0;
        for (var y = 0; y < s.Height; y++)
        {
            for (var x = 0; x < s.Width; x++)
            {
                var (r, g, b, _) = s.Px(x, y);
                most = Math.Max(most, Math.Max(Math.Abs(r - g), Math.Max(Math.Abs(g - b), Math.Abs(r - b))));
            }
        }

        return most;
    }

    private static int Ink(Surface s, (int R, int G, int B) background)
    {
        var count = 0;
        for (var y = 0; y < s.Height; y++)
        {
            for (var x = 0; x < s.Width; x++)
            {
                var (r, g, b, _) = s.Px(x, y);
                if (r != background.R || g != background.G || b != background.B)
                    count++;
            }
        }

        return count;
    }

    [Fact]
    public async Task Lcd_text_over_an_opaque_backdrop_has_a_coverage_per_color_channel()
    {
        using var context = await ContextAsync(SkiaTextRendering.Lcd);
        foreach (var (text, backdrop) in new[]
        {
            (PaintColor.Black, new SKColor(255, 255, 255)),
            (PaintColor.White, new SKColor(20, 20, 30)),
            (PaintColor.FromArgb(255, 200, 30, 30), new SKColor(255, 255, 255)),
        })
        {
            using var s = new Surface(context, 120, 30, backdrop);
            using (var canvas = s.Canvas())
                Draw(canvas, context, "Hamburgefonts", text);

            // Blue against white (and white against dark) differ in every channel, so a channel's own coverage shows as a colored fringe.
            Assert.True(Fringe(s) > 40, $"fringe {Fringe(s)} for {text}");
            Assert.True(Ink(s, (backdrop.Red, backdrop.Green, backdrop.Blue)) > 100);
            Assert.True(context.TextMasks.Count > 0);
        }
    }

    [Fact]
    public async Task Lcd_text_never_touches_the_alpha_of_an_opaque_surface()
    {
        using var context = await ContextAsync(SkiaTextRendering.Lcd);
        using var s = new Surface(context, 120, 30, White);
        using (var canvas = s.Canvas())
            Draw(canvas, context, "Hamburgefonts", PaintColor.Black);

        for (var y = 0; y < s.Height; y++)
        {
            for (var x = 0; x < s.Width; x++)
                Assert.Equal(255, s.Px(x, y).A);
        }
    }

    [Fact]
    public async Task Bgr_stripes_swap_the_red_and_blue_coverage()
    {
        using var rgb = await ContextAsync(SkiaTextRendering.Lcd);
        using var bgr = await ContextAsync(SkiaTextRendering.Lcd);
        bgr.LcdBgrStripes = true;
        using var a = new Surface(rgb, 100, 30, White);
        using var b = new Surface(bgr, 100, 30, White);
        using (var canvas = a.Canvas())
            Draw(canvas, rgb, "Hamburgefonts", PaintColor.Black);
        using (var canvas = b.Canvas())
            Draw(canvas, bgr, "Hamburgefonts", PaintColor.Black);

        var different = 0;
        for (var y = 0; y < a.Height; y++)
        {
            for (var x = 0; x < a.Width; x++)
            {
                var (r1, g1, b1, _) = a.Px(x, y);
                var (r2, g2, b2, _) = b.Px(x, y);

                // The filter is symmetric, so reversing the stripes reverses the channels exactly (give or take the rounding of the two blend passes).
                Assert.InRange(r2 - b1, -1, 1);
                Assert.InRange(b2 - r1, -1, 1);
                Assert.InRange(g2 - g1, -1, 1);
                if (Math.Abs(r1 - b1) > 20)
                    different++;
            }
        }

        Assert.True(different > 20, "the two orders are not the same picture");
    }

    [Fact]
    public async Task Grayscale_text_is_neutral_and_differs_from_the_outline()
    {
        using var gray = await ContextAsync(SkiaTextRendering.Grayscale);
        using var outline = await ContextAsync(SkiaTextRendering.Outline);
        using var g = new Surface(gray, 100, 30, White);
        using var o = new Surface(outline, 100, 30, White);
        using (var canvas = g.Canvas())
            Draw(canvas, gray, "Hamburgefonts", PaintColor.Black);
        using (var canvas = o.Canvas())
            Draw(canvas, outline, "Hamburgefonts", PaintColor.Black);

        Assert.True(Fringe(g) <= 1, $"grayscale fringe {Fringe(g)}");
        Assert.True(Fringe(o) <= 1);
        Assert.True(Ink(g, (255, 255, 255)) > 100 && Ink(o, (255, 255, 255)) > 100);
        Assert.True(gray.TextMasks.Count > 0);
        Assert.Equal(0, outline.TextMasks.Count);

        var differ = 0;
        for (var y = 0; y < 30; y++)
        {
            for (var x = 0; x < 100; x++)
            {
                if (Math.Abs(g.Px(x, y).R - o.Px(x, y).R) > 12)
                    differ++;
            }
        }

        Assert.True(differ > 50, "the hinted, corrected mask draws the same pixels as the outline");
    }

    [Fact]
    public async Task Grayscale_text_blends_over_a_translucent_backdrop_too()
    {
        using var context = await ContextAsync(SkiaTextRendering.Grayscale);
        using var s = new Surface(context, 100, 30, SKColors.Transparent);
        using (var canvas = s.Canvas(opaque: false))
            Draw(canvas, context, "Hamburgefonts", PaintColor.Black);

        var alpha = 0;
        for (var x = 0; x < 100; x++)
            alpha = Math.Max(alpha, Enumerable.Range(0, 30).Max(y => s.Px(x, y).A));

        Assert.Equal(255, alpha);
    }

    [Fact]
    public async Task Lcd_falls_back_to_grayscale_without_an_opaque_backdrop_and_inside_a_layer()
    {
        using var context = await ContextAsync(SkiaTextRendering.Lcd);

        // Not told the backdrop is opaque.
        using (var s = new Surface(context, 100, 30, White))
        {
            using (var canvas = s.Canvas(opaque: false))
                Draw(canvas, context, "Hamburgefonts", PaintColor.Black);

            Assert.True(Fringe(s) <= 1);
            Assert.True(Ink(s, (255, 255, 255)) > 100);
        }

        // Inside a translucent layer, even though the canvas was told the backdrop is opaque.
        using (var s = new Surface(context, 100, 30, White))
        {
            using (var canvas = s.Canvas())
            {
                using var layer = canvas.BeginLayer(new LayerOptions(Opacity: 0.5));
                Draw(layer!.Canvas, context, "Hamburgefonts", PaintColor.Black);
            }

            Assert.True(Fringe(s) <= 1);
            Assert.True(Ink(s, (255, 255, 255)) > 100);
        }
    }

    [Fact]
    public async Task Text_under_a_rotation_a_skew_or_an_uneven_scale_stays_an_outline()
    {
        using var context = await ContextAsync(SkiaTextRendering.Lcd);
        foreach (var matrix in new[] { Matrix3x2.CreateRotation(0.4f), Matrix3x2.CreateSkew(0.3f, 0), Matrix3x2.CreateScale(1f, 1.5f) })
        {
            using var s = new Surface(context, 120, 60, White);
            using (var canvas = s.Canvas())
            {
                canvas.PushTransform(matrix);
                Draw(canvas, context, "Hamburgefonts", PaintColor.Black, x: 10, y: 10);
                canvas.PopTransform();
            }

            Assert.Equal(0, context.TextMasks.Count);
            Assert.True(Fringe(s) <= 1);
            Assert.True(Ink(s, (255, 255, 255)) > 50);
        }
    }

    [Fact]
    public async Task A_color_font_and_a_blend_mode_and_a_recorded_tile_keep_the_outline_path()
    {
        using var context = await ContextAsync(SkiaTextRendering.Grayscale);
        await using (var stream = File.OpenRead(BundledFonts.ColorV0))
            await context.AddFont(stream, "Color Test");

        using var s = new Surface(context, 80, 80, White);
        using (var canvas = s.Canvas())
        {
            var color = context.GetFont("Color Test", 30, PaintFontStyle.Regular)!;
            canvas.DrawString("A", color, PaintColor.Black, new PaintPoint(5, 5), new Core.Size(30, 30));
            Assert.Equal(0, context.TextMasks.Count);

            using var tile = canvas.CreateTile(60, 20)!.Value.Graphics;
            Draw(tile, context, "Hi", PaintColor.Black);
            Assert.Equal(0, context.TextMasks.Count);

            canvas.PushBlendMode(PaintBlendMode.Multiply);
            Draw(canvas, context, "Hi", PaintColor.Black);
            canvas.PopBlendMode();
            Assert.Equal(0, context.TextMasks.Count);

            Draw(canvas, context, "Hi", PaintColor.Black);
            Assert.True(context.TextMasks.Count > 0);
        }
    }

    [Fact]
    public async Task Auto_leaves_large_text_as_an_outline_and_a_named_mode_does_not()
    {
        using var auto = await ContextAsync(SkiaTextRendering.Auto, subpixel: true);
        using (var s = new Surface(auto, 300, 100, White))
        {
            using var canvas = s.Canvas();
            Draw(canvas, auto, "Big", PaintColor.Black, size: 80);
            Assert.Equal(0, auto.TextMasks.Count);
            Draw(canvas, auto, "Small", PaintColor.Black, size: 14, y: 80);
            Assert.True(auto.TextMasks.Count > 0);
        }

        using var named = await ContextAsync(SkiaTextRendering.Grayscale);
        using (var s = new Surface(named, 300, 100, White))
        {
            using var canvas = s.Canvas();
            Draw(canvas, named, "Big", PaintColor.Black, size: 80);
            Assert.True(named.TextMasks.Count > 0);
        }
    }

    [Theory]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(2.0)]
    public async Task A_uniform_scale_draws_a_hinted_mask_at_the_device_size(double scale)
    {
        using var context = await ContextAsync(SkiaTextRendering.Lcd);
        using var one = new Surface(context, 200, 80, White);
        using var scaled = new Surface(context, 200, 80, White, scale);
        using (var canvas = one.Canvas())
            Draw(canvas, context, "Hxg", PaintColor.Black, size: 28);

        var after = context.TextMasks.Count;
        using (var canvas = scaled.Canvas())
            Draw(canvas, context, "Hxg", PaintColor.Black, size: 28);

        // A mask of its own, made for the device size (the one for the unscaled run is not reused).
        Assert.True(context.TextMasks.Count > after);
        Assert.True(Fringe(scaled) > 40);

        (int Left, int Right, int Top, int Bottom) Bounds(Surface s)
        {
            int l = int.MaxValue, r = -1, t = int.MaxValue, b = -1;
            for (var y = 0; y < s.Height; y++)
            {
                for (var x = 0; x < s.Width; x++)
                {
                    if (s.Px(x, y).R > 200)
                        continue;

                    l = Math.Min(l, x);
                    r = Math.Max(r, x);
                    t = Math.Min(t, y);
                    b = Math.Max(b, y);
                }
            }

            return (l, r, t, b);
        }

        var a = Bounds(one);
        var c = Bounds(scaled);
        Assert.InRange((c.Right - c.Left) / (double)(a.Right - a.Left), scale - 0.12, scale + 0.12);
        Assert.InRange((c.Bottom - c.Top) / (double)(a.Bottom - a.Top), scale - 0.15, scale + 0.15);

        // The H's stem is fitted to the grid: a column that is fully black from the cap height to the baseline.
        var solid = false;
        for (var x = c.Left; x < c.Left + 8 && !solid; x++)
        {
            var run = 0;
            for (var y = c.Top; y <= c.Bottom; y++)
                run += s_isDark(scaled.Px(x, y)) ? 1 : 0;

            solid = run >= (c.Bottom - c.Top) * 0.6;
        }

        Assert.True(solid, "no crisp stem");
    }

    private static readonly Func<(int R, int G, int B, int A), bool> s_isDark = p => p.R < 25 && p.G < 25 && p.B < 25;

    [Fact]
    public async Task The_mask_cache_serves_a_run_drawn_again_and_is_emptied_with_the_context()
    {
        var context = await ContextAsync(SkiaTextRendering.Lcd);
        using (var s = new Surface(context, 100, 30, White))
        {
            using var canvas = s.Canvas();
            Draw(canvas, context, "Cache me", PaintColor.Black);
            var misses = context.TextMasks.Misses;
            Assert.Equal(0, context.TextMasks.Hits);
            Draw(canvas, context, "Cache me", PaintColor.FromArgb(255, 200, 0, 0));
            Assert.Equal(misses, context.TextMasks.Misses);
            Assert.Equal(1, context.TextMasks.Hits);
            Assert.True(context.TextMasks.ByteCount > 0);
            Assert.True(context.GlyphMasks.Count > 0);
        }

        Assert.Equal(0, context.Counts.Total);
        context.Dispose();
        Assert.Equal(0, context.TextMasks.Count);
        Assert.Equal(0, context.GlyphMasks.Count);
        Assert.Equal(0, context.TextMasks.ByteCount);
    }

    [Fact]
    public async Task A_run_cut_by_the_clip_is_cut_in_the_mask_and_a_run_outside_it_draws_nothing()
    {
        using var context = await ContextAsync(SkiaTextRendering.Lcd);
        using var s = new Surface(context, 100, 30, White);
        using (var canvas = s.Canvas())
        {
            canvas.PushClip(new Rect(0, 0, 30, 30));
            Draw(canvas, context, "Hamburgefonts", PaintColor.Black);
            Draw(canvas, context, "Outside", PaintColor.Black, x: 60);
            canvas.PopClip();
        }

        for (var x = 30; x < 100; x++)
        {
            for (var y = 0; y < 30; y++)
                Assert.Equal((255, 255, 255, 255), s.Px(x, y));
        }
    }

    [Fact]
    public async Task Auto_follows_the_systems_setting_and_a_named_mode_overrides_it()
    {
        // Subpixel text asked for by the system.
        using (var context = await ContextAsync(SkiaTextRendering.Auto, subpixel: true))
        {
            using var s = new Surface(context, 100, 30, White);
            using (var canvas = s.Canvas())
                Draw(canvas, context, "Hamburgefonts", PaintColor.Black);

            Assert.True(Fringe(s) > 40);
        }

        // Not asked for.
        using (var context = await ContextAsync(SkiaTextRendering.Auto, subpixel: false))
        {
            using var s = new Surface(context, 100, 30, White);
            using (var canvas = s.Canvas())
                Draw(canvas, context, "Hamburgefonts", PaintColor.Black);

            Assert.True(Fringe(s) <= 1);
            Assert.True(context.TextMasks.Count > 0);
        }

        // Asked for, but not over a backdrop known to be opaque.
        using (var context = await ContextAsync(SkiaTextRendering.Auto, subpixel: true))
        {
            using var s = new Surface(context, 100, 30, White);
            using (var canvas = s.Canvas(opaque: false))
                Draw(canvas, context, "Hamburgefonts", PaintColor.Black);

            Assert.True(Fringe(s) <= 1);
        }

        // The system's stripe order is followed.
        using (var rgb = await ContextAsync(SkiaTextRendering.Auto, subpixel: true, bgr: false))
        using (var bgr = await ContextAsync(SkiaTextRendering.Auto, subpixel: true, bgr: true))
        {
            using var a = new Surface(rgb, 100, 30, White);
            using var b = new Surface(bgr, 100, 30, White);
            using (var canvas = a.Canvas())
                Draw(canvas, rgb, "Hamburgefonts", PaintColor.Black);
            using (var canvas = b.Canvas())
                Draw(canvas, bgr, "Hamburgefonts", PaintColor.Black);

            var swapped = 0;
            for (var y = 0; y < 30; y++)
            {
                for (var x = 0; x < 100; x++)
                {
                    if (Math.Abs(b.Px(x, y).R - a.Px(x, y).B) <= 1 && Math.Abs(a.Px(x, y).R - a.Px(x, y).B) > 20)
                        swapped++;
                }
            }

            Assert.True(swapped > 20);
        }

        // A named mode ignores the system.
        using (var context = await ContextAsync(SkiaTextRendering.Outline, subpixel: true))
        {
            using var s = new Surface(context, 100, 30, White);
            using (var canvas = s.Canvas())
                Draw(canvas, context, "Hamburgefonts", PaintColor.Black);

            Assert.Equal(0, context.TextMasks.Count);
        }

        using (var context = await ContextAsync(SkiaTextRendering.Grayscale, subpixel: true))
        {
            using var s = new Surface(context, 100, 30, White);
            using (var canvas = s.Canvas())
                Draw(canvas, context, "Hamburgefonts", PaintColor.Black);

            Assert.True(Fringe(s) <= 1);
        }
    }

    [Fact]
    public async Task A_composers_export_gets_no_fringes_from_auto_but_does_from_a_named_lcd()
    {
        foreach (var (mode, expectFringe) in new[] { (SkiaTextRendering.Auto, false), (SkiaTextRendering.Lcd, true) })
        {
            using var context = await ContextAsync(mode, subpixel: true);
            using var surface = SKSurface.Create(new SKImageInfo(100, 30, SKColorType.Rgba8888, SKAlphaType.Premul))!;
            using (var composer = new SkiaComposer(context, surface, 100, 30, new SkiaComposerOptions { Background = PaintColor.White }))
            {
                Draw(composer.Canvas, context, "Hamburgefonts", PaintColor.Black);
                var pixels = composer.ToPixelBuffer().PremultipliedRgba.Span;
                var fringe = 0;
                for (var i = 0; i < pixels.Length; i += 4)
                    fringe = Math.Max(fringe, Math.Abs(pixels[i] - pixels[i + 2]));

                Assert.Equal(expectFringe, fringe > 40);
                Assert.True(context.TextMasks.Count > 0);
            }
        }

        // A transparent page has no opaque backdrop, so even a named Lcd is grayscale.
        using var transparent = await ContextAsync(SkiaTextRendering.Lcd);
        using var surface2 = SKSurface.Create(new SKImageInfo(100, 30, SKColorType.Rgba8888, SKAlphaType.Premul))!;
        using var composer2 = new SkiaComposer(transparent, surface2, 100, 30);
        Draw(composer2.Canvas, transparent, "Hamburgefonts", PaintColor.Black);
        var bytes = composer2.ToPixelBuffer().PremultipliedRgba.Span;
        var most = 0;
        for (var i = 0; i < bytes.Length; i += 4)
            most = Math.Max(most, Math.Abs(bytes[i] - bytes[i + 2]));

        Assert.True(most <= 1);
    }

    [Fact]
    public void The_gsettings_output_is_read_for_smoothing_and_stripe_order()
    {
        Assert.True(SystemTextSettingsProvider.ParseGsettingsSmoothing("'rgba'\n"));
        Assert.True(SystemTextSettingsProvider.ParseGsettingsSmoothing("  \"RGBA\" "));
        Assert.False(SystemTextSettingsProvider.ParseGsettingsSmoothing("'grayscale'\n"));
        Assert.False(SystemTextSettingsProvider.ParseGsettingsSmoothing("'none'"));
        Assert.False(SystemTextSettingsProvider.ParseGsettingsSmoothing(""));

        Assert.True(SystemTextSettingsProvider.ParseGsettingsBgr("'bgr'\n"));
        Assert.False(SystemTextSettingsProvider.ParseGsettingsBgr("'rgb'\n"));
        Assert.False(SystemTextSettingsProvider.ParseGsettingsBgr("'vbgr'\n"));
        Assert.False(SystemTextSettingsProvider.ParseGsettingsBgr(null));
    }

    [Fact]
    public void Linux_settings_compose_from_the_two_gsettings_answers()
    {
        var asked = new List<string>();
        string? Order(string key)
        {
            asked.Add(key);
            return "'bgr'";
        }

        Assert.Equal(new SystemTextSettings(true, true), SystemTextSettingsProvider.FromGsettings("'rgba'", Order));
        Assert.Equal(new SystemTextSettings(true, false), SystemTextSettingsProvider.FromGsettings("'rgba'", _ => "'rgb'"));
        Assert.Equal(new SystemTextSettings(true, false), SystemTextSettingsProvider.FromGsettings("'rgba'", _ => null));
        asked.Clear();
        Assert.Equal(default, SystemTextSettingsProvider.FromGsettings("'grayscale'", Order));
        Assert.Equal(default, SystemTextSettingsProvider.FromGsettings(null, Order));
        Assert.Empty(asked);
    }

    private sealed class FakeGsettings(string output, bool exits = true, int exitCode = 0) : SystemTextSettingsProvider.IGsettingsProcess
    {
        public bool Killed { get; private set; }

        public bool Disposed { get; private set; }

        public int ExitCode => exitCode;

        public Task<string> ReadOutputAsync() => Task.FromResult(output);

        public bool WaitForExit(int milliseconds) => exits;

        public void Kill() => Killed = true;

        public void Dispose() => Disposed = true;
    }

    [Fact]
    public async Task The_mask_cache_drops_its_oldest_runs_when_it_holds_more_than_its_budget_and_keeps_the_newest()
    {
        using var context = await ContextAsync(SkiaTextRendering.Grayscale);
        var font = context.GetFont("Source Sans 3", 20, PaintFontStyle.Regular)!;

        // Room for a few runs: each run of this size is a few hundred bytes, so a mask is stored (a mask is kept only when it is at most an eighth of the budget).
        var cache = new SkiaTextMaskCache(20_000);
        var words = Enumerable.Range(0, 40).Select(i => "word" + i).ToList();
        foreach (var word in words)
            Assert.NotNull(cache.Get(context, word, font, 0, 1, 1, 1, 0, 0, null, default, 1, null, out _));

        Assert.True(cache.ByteCount <= 20_000, $"{cache.ByteCount} bytes are held");
        Assert.InRange(cache.Count, 1, words.Count - 1);

        // The newest run is still held (asking for it again is a hit), the oldest is not (it is made again).
        var hits = cache.Hits;
        _ = cache.Get(context, words[^1], font, 0, 1, 1, 1, 0, 0, null, default, 1, null, out _);
        Assert.Equal(hits + 1, cache.Hits);
        var misses = cache.Misses;
        _ = cache.Get(context, words[0], font, 0, 1, 1, 1, 0, 0, null, default, 1, null, out _);
        Assert.Equal(misses + 1, cache.Misses);

        // A run too big to be worth keeping (over an eighth of the budget) is made and handed back, not stored.
        var tiny = new SkiaTextMaskCache(100);
        Assert.NotNull(tiny.Get(context, "not stored", font, 0, 1, 1, 1, 0, 0, null, default, 1, null, out _));
        Assert.Equal(0, tiny.Count);
    }

    [Fact]
    public async Task A_run_of_blanks_has_an_empty_mask_and_a_run_outside_the_visible_limit_has_none_to_draw()
    {
        using var context = await ContextAsync(SkiaTextRendering.Grayscale);
        var font = context.GetFont("Source Sans 3", 20, PaintFontStyle.Regular)!;
        var cache = new SkiaTextMaskCache();

        // Spaces carry no ink: the mask is empty, with or without a limit.
        var blank = cache.Get(context, "   ", font, 0, 1, 1, 1, 10, 10, null, default, 1, null, out _);
        Assert.Equal(0, blank!.Value.Width);
        blank = cache.Get(context, "   ", font, 0, 1, 1, 1, 10, 10, null, default, 1, (0, 0, 100, 100), out _);
        Assert.Equal(0, blank!.Value.Width);

        // Text whose place is wholly outside what can be seen is cut to nothing; inside, it keeps its ink.
        var outside = cache.Get(context, "visible", font, 0, 1, 1, 1, 10, 10, null, default, 1, (500, 500, 600, 600), out _);
        Assert.Equal(0, outside!.Value.Width);
        var inside = cache.Get(context, "visible", font, 0, 1, 1, 1, 10, 10, null, default, 1, (0, 0, 200, 200), out _);
        Assert.True(inside!.Value.Width > 20);
        Assert.Contains(inside.Value.Samples, b => b != 0);

        // Cut by a limit that takes only the left of the word, the mask is narrower than the whole.
        var cut = cache.Get(context, "visible", font, 0, 1, 1, 1, 10, 10, null, default, 1, (0, 0, 25, 200), out _);
        Assert.True(cut!.Value.Width < inside.Value.Width);
    }

    [Fact]
    public void A_gsettings_answer_is_its_output_only_when_the_process_ended_in_time_with_success()
    {
        var ok = new FakeGsettings("'rgba'\n");
        Assert.Equal("'rgba'\n", SystemTextSettingsProvider.Gsettings("font-antialiasing", _ => ok, 1000));
        Assert.True(ok.Disposed);
        Assert.False(ok.Killed);

        // A process that does not end within the time is killed, and there is no answer.
        var slow = new FakeGsettings("'rgba'", exits: false);
        Assert.Null(SystemTextSettingsProvider.Gsettings("font-antialiasing", _ => slow, 10));
        Assert.True(slow.Killed);
        Assert.True(slow.Disposed);

        // A failing exit code is no answer either.
        Assert.Null(SystemTextSettingsProvider.Gsettings("font-antialiasing", _ => new FakeGsettings("'rgba'", exitCode: 1), 1000));

        // No process started, and a start that throws (no gsettings installed), are no answer.
        Assert.Null(SystemTextSettingsProvider.Gsettings("font-antialiasing", _ => null, 1000));
        Assert.Null(SystemTextSettingsProvider.Gsettings("font-antialiasing", _ => throw new InvalidOperationException("no gsettings"), 1000));

        // The key asked for reaches the starter.
        string? asked = null;
        _ = SystemTextSettingsProvider.Gsettings("font-rgba-order", key => { asked = key; return null; }, 1000);
        Assert.Equal("font-rgba-order", asked);
    }

    [Fact]
    public void Detection_reads_the_platform_it_was_told_it_is_on_and_falls_back_to_grayscale_when_that_fails()
    {
        var windows = new SystemTextSettings(true, true);
        var linux = new SystemTextSettings(true, false);
        Assert.Equal(windows, SystemTextSettingsProvider.Detect(true, false, () => windows, () => linux));
        Assert.Equal(linux, SystemTextSettingsProvider.Detect(false, true, () => windows, () => linux));
        Assert.Equal(default, SystemTextSettingsProvider.Detect(false, false, () => windows, () => linux));
        Assert.Equal(default, SystemTextSettingsProvider.Detect(false, true, () => windows, () => throw new InvalidOperationException()));
        Assert.Equal(default, SystemTextSettingsProvider.Detect(true, false, () => throw new InvalidOperationException(), () => linux));
    }

    [Fact]
    public void Detection_never_throws_whatever_the_machine_has()
    {
        // No gsettings here (or one that answers): either way a settings value, never an exception.
        _ = SystemTextSettingsProvider.DetectLinux();
        _ = SystemTextSettingsProvider.Gsettings("font-antialiasing");
        _ = SystemTextSettingsProvider.Detect();
    }

    [Fact]
    public async Task Synthetic_styles_and_palettes_and_huge_runs_are_drawn_whole_or_as_outlines()
    {
        using var context = await ContextAsync(SkiaTextRendering.Grayscale);
        using var s = new Surface(context, 300, 120, White);
        using (var canvas = s.Canvas())
        {
            // Only the regular face is loaded, so italic and bold are synthesized: the letters cannot be built from masks of the regular glyphs, the word is rasterized whole.
            var italic = context.GetFont("Source Sans 3", 20, PaintFontStyle.Italic)!;
            var bold = context.GetFont("Source Sans 3", 20, PaintFontStyle.Bold)!;
            canvas.DrawString("Slanted", italic, PaintColor.Black, new PaintPoint(4, 4), canvas.MeasureString("Slanted", italic));
            canvas.DrawString("Heavy", bold, PaintColor.Black, new PaintPoint(150, 4), canvas.MeasureString("Heavy", bold));
            Assert.True(context.TextMasks.Count >= 2);

            // A palette is a color-font feature, so the run is whole too.
            var font = context.GetFont("Source Sans 3", 20, PaintFontStyle.Regular)!;
            canvas.DrawString("Palette", font, PaintColor.Black, new PaintPoint(4, 40), canvas.MeasureString("Palette", font), fontPalette: new FontPalette(0, []));
        }

        Assert.True(Ink(s, (255, 255, 255)) > 200);

        // Text too large for a mask is drawn as an outline instead, even when a mode was named.
        var before = context.TextMasks.Count;
        using var big = new Surface(context, 400, 400, White);
        using (var canvas = big.Canvas())
        {
            var huge = context.GetFont("Source Sans 3", 4000, PaintFontStyle.Regular)!;
            canvas.DrawString("W", huge, PaintColor.Black, new PaintPoint(-300, -2900), new Core.Size(100, 100));
        }

        Assert.Equal(before, context.TextMasks.Count);
        Assert.True(Ink(big, (255, 255, 255)) > 1000);
    }

    [Fact]
    public async Task A_position_that_rounds_up_to_the_next_pixel_lands_on_that_pixel()
    {
        using var context = await ContextAsync(SkiaTextRendering.Grayscale);
        using var a = new Surface(context, 100, 30, White);
        using var b = new Surface(context, 100, 30, White);

        // 10.999 is a 64th of a pixel from 11: its mask is the one for 11, shifted a whole pixel.
        using (var canvas = a.Canvas())
            Draw(canvas, context, "Hamburg", PaintColor.Black, x: 10.999, y: 5.999);
        using (var canvas = b.Canvas())
            Draw(canvas, context, "Hamburg", PaintColor.Black, x: 11, y: 6);

        for (var y = 0; y < 30; y++)
        {
            for (var x = 0; x < 100; x++)
                Assert.Equal(b.Px(x, y), a.Px(x, y));
        }
    }

    [Fact]
    public async Task A_fully_transparent_color_draws_nothing_and_an_empty_clip_draws_nothing()
    {
        using var context = await ContextAsync(SkiaTextRendering.Lcd);
        using var s = new Surface(context, 100, 30, White);
        using (var canvas = s.Canvas())
        {
            Draw(canvas, context, "Invisible", PaintColor.FromArgb(0, 0, 0, 0));
            canvas.PushClip(new Rect(50, 0, 0, 0));
            Draw(canvas, context, "Clipped away", PaintColor.Black);
            canvas.PopClip();
        }

        Assert.Equal(0, Ink(s, (255, 255, 255)));
    }

    [Fact]
    public void The_default_system_provider_answers_without_throwing_or_hanging()
    {
        var started = DateTime.UtcNow;
        var settings = SystemTextSettingsProvider.Instance.Get();

        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(5));
        if (OperatingSystem.IsMacOS())
            Assert.False(settings.Subpixel);
    }

    [Fact]
    public void The_correction_makes_dark_and_light_text_differ_and_stays_in_range()
    {
        var dark = SkiaTextBlend.CorrectionTable(PaintColor.Black);
        var light = SkiaTextBlend.CorrectionTable(PaintColor.White);
        Assert.Equal(0, dark[0]);
        Assert.Equal(255, dark[255]);
        Assert.Equal(255, light[255]);
        Assert.NotEqual(dark[128], light[128]);

        var lcd = SkiaTextBlend.CorrectionTable(PaintColor.Black, lcd: true);
        Assert.NotEqual(dark[128], lcd[128]);
    }
}
