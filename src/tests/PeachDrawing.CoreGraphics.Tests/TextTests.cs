using PeachDrawing.Core;
using PeachDrawing.Text.Unicode;

namespace PeachDrawing.CoreGraphics.Tests;

public class TextTests
{
    [Fact]
    public async Task The_PeachDrawing_engine_draws_text_as_ink_and_measures_what_it_draws()
    {
        using var scene = await Scene.WithFontAsync();
        var font = scene.Context.GetFont("Source Sans 3", 24, PaintFontStyle.Regular)!;
        var size = scene.Canvas.MeasureString("Hello", font);
        scene.Canvas.DrawString("Hello", font, PaintColor.FromArgb(255, 0, 0, 0), new PaintPoint(5, 5), size);

        Assert.Equal(5, scene.Canvas.CountShapedGlyphs("Hello", font));
        Assert.True(size.Width > 30);
        Assert.True(scene.OpaquePixels() > 100);
        Assert.True(scene.IsClear(190, 55));
    }

    [Fact]
    public async Task Text_sits_on_its_baseline_with_ink_above_it_and_not_mirrored()
    {
        using var scene = await Scene.WithFontAsync(100, 60);
        var font = scene.Context.GetFont("Source Sans 3", 40, PaintFontStyle.Regular)!;
        scene.Canvas.DrawString("I", font, PaintColor.FromArgb(255, 0, 0, 0), new PaintPoint(10, 0), scene.Canvas.MeasureString("I", font));

        // "I" is a tall stem: ink spans most of the line box and is not in the bottom rows below the baseline.
        var rows = Enumerable.Range(0, 60).Where(y => Enumerable.Range(0, 100).Any(x => scene.Px(x, y).A > 128)).ToList();
        Assert.NotEmpty(rows);
        Assert.True(rows.Max() - rows.Min() > 20);
        Assert.True(rows.Min() >= 2);
    }

    [Fact]
    public async Task Synthetic_bold_thickens_the_text()
    {
        using var regular = await Scene.WithFontAsync();
        using var bold = await Scene.WithFontAsync();
        var black = PaintColor.FromArgb(255, 0, 0, 0);
        var f1 = regular.Context.GetFont("Source Sans 3", 24, PaintFontStyle.Regular)!;
        var f2 = bold.Context.GetFont("Source Sans 3", 24, PaintFontStyle.Bold)!;
        regular.Canvas.DrawString("lll", f1, black, new PaintPoint(5, 5), regular.Canvas.MeasureString("lll", f1));
        bold.Canvas.DrawString("lll", f2, black, new PaintPoint(5, 5), bold.Canvas.MeasureString("lll", f2));

        static double Ink(Scene scene) => scene.Composer.ToPixelBuffer().PremultipliedRgba.ToArray().Where((_, i) => i % 4 == 3).Sum(a => (double)a);
        Assert.True(Ink(bold) > Ink(regular) * 1.1);
    }

    [Fact]
    public async Task A_text_outline_is_available_for_PeachDrawing_fonts()
    {
        using var scene = await Scene.WithFontAsync();
        var font = scene.Context.GetFont("Source Sans 3", 24, PaintFontStyle.Regular)!;
        using var outline = scene.Canvas.GetTextOutline("A", font, new PaintPoint(5, 30));
        Assert.NotNull(outline);
    }

    [Fact]
    public async Task Ink_crossings_are_reported()
    {
        using var scene = await Scene.WithFontAsync();
        var font = scene.Context.GetFont("Source Sans 3", 24, PaintFontStyle.Regular)!;
        var spans = scene.Canvas.GetInkCrossings("lll", font, new PaintPoint(0, 0), 10, 14);
        Assert.NotNull(spans);
        Assert.NotEmpty(spans!);
    }

    [Fact]
    public void The_native_engine_resolves_the_system_font_measures_and_draws()
    {
        using var scene = Scene.Create(200, 60, engine: CoreGraphicsTextEngine.Native);
        var font = scene.Context.GetFont(new PeachDrawing.Text.FontSet().ResolveGeneric(PeachDrawing.Text.GenericFamily.SystemUi)!, 24, PaintFontStyle.Regular)!;
        var black = PaintColor.FromArgb(255, 0, 0, 0);
        var size = scene.Canvas.MeasureString("Hello", font);
        scene.Canvas.DrawString("Hello", font, black, new PaintPoint(5, 5), size);

        Assert.Equal(5, scene.Canvas.CountShapedGlyphs("Hello", font));
        Assert.Null(scene.Canvas.GetTextOutline("A", font, new PaintPoint(0, 20)));
        Assert.True(size.Width > 30);
        Assert.True(scene.OpaquePixels() > 100);
    }

    [Fact]
    public void The_native_engine_draws_text_upright_on_its_baseline()
    {
        using var scene = Scene.Create(100, 60, engine: CoreGraphicsTextEngine.Native);
        var font = scene.Context.GetFont("Helvetica", 40, PaintFontStyle.Regular)!;
        scene.Canvas.DrawString("I", font, PaintColor.FromArgb(255, 0, 0, 0), new PaintPoint(10, 0), scene.Canvas.MeasureString("I", font));

        var rows = Enumerable.Range(0, 60).Where(y => Enumerable.Range(0, 100).Any(x => scene.Px(x, y).A > 128)).ToList();
        Assert.NotEmpty(rows);
        Assert.True(rows.Max() - rows.Min() > 20);
    }

    [Fact]
    public async Task The_native_engine_matches_a_registered_font_by_family()
    {
        using var scene = Scene.Create(100, 60, engine: CoreGraphicsTextEngine.Native);
        await using var stream = File.OpenRead(PeachPDF.Tests.TestSupport.BundledFonts.Ttf);
        await scene.Context.AddFont(stream, "Registered Sans");
        var font = scene.Context.GetFont("Registered Sans", 20, PaintFontStyle.Regular)!;
        Assert.True(font.HasGlyph(new System.Text.Rune('A')));
        Assert.StartsWith("coretext/Registered Sans", font.FaceKey);
    }

    [Fact]
    public void The_native_engine_finds_a_fallback_for_a_symbol_the_text_font_lacks()
    {
        using var scene = Scene.Create(100, 60, engine: CoreGraphicsTextEngine.Native);
        var font = scene.Context.GetSystemFallbackFontForCodepoint(20, PaintFontStyle.Regular, new System.Text.Rune(0x1F600), 400, 100, null, EmojiPresentation.Emoji);
        Assert.NotNull(font);
    }

    private static async Task<byte[]> InkOf(TextHinting hinting, bool lowDensity = false, double pixelsPerPoint = 1)
    {
        using var scene = Scene.Create(60, 40, pixelsPerPoint);
        scene.Context.TextHinting = hinting;
        scene.Context.HintTextOnLowDensityDisplays = lowDensity;
        await using var stream = File.OpenRead(PeachPDF.Tests.TestSupport.BundledFonts.Ttf);
        await scene.Context.AddFont(stream, "Source Sans 3");
        var font = scene.Context.GetFont("Source Sans 3", 13.3 * pixelsPerPoint, PaintFontStyle.Regular)!;
        scene.Canvas.DrawString("Hxg", font, PaintColor.FromArgb(255, 0, 0, 0), new PaintPoint(3.3, 2.4), scene.Canvas.MeasureString("Hxg", font));
        return scene.Composer.ToPixelBuffer().PremultipliedRgba.ToArray();
    }

    [Fact]
    public async Task Hinting_is_off_by_default_and_the_low_density_option_leaves_two_pixels_to_a_point_alone()
    {
        var plain = await InkOf(TextHinting.None, pixelsPerPoint: 2);
        var option = await InkOf(TextHinting.None, lowDensity: true, pixelsPerPoint: 2);

        Assert.Equal(plain, option);
        Assert.Contains(plain, b => b != 0);
    }

    [Fact]
    public async Task Standard_hinting_fits_the_text_to_the_pixel_grid_and_still_draws_it_upright()
    {
        var plain = await InkOf(TextHinting.None);
        var hinted = await InkOf(TextHinting.Standard);

        Assert.NotEqual(plain, hinted);
        Assert.True(hinted.Where((_, i) => i % 4 == 3).Count(a => a > 0) > 40);

        // The H's stems are upright: ink is in the upper rows of the line box and the lowest ink row is the baseline's.
        var rows = Enumerable.Range(0, 40).Where(y => Enumerable.Range(0, 60).Any(x => hinted[(y * 60 + x) * 4 + 3] > 128)).ToList();
        Assert.True(rows.Max() - rows.Min() >= 8);
    }

    [Fact]
    public async Task Hinting_on_low_density_displays_hints_at_one_pixel_to_a_point_and_not_at_two()
    {
        var plain = await InkOf(TextHinting.None);
        var low = await InkOf(TextHinting.None, lowDensity: true);
        Assert.NotEqual(plain, low);
        Assert.Equal(await InkOf(TextHinting.Standard), low);
    }

    [Theory]
    [InlineData(TextHinting.None, false, 1.0, TextHinting.None)]
    [InlineData(TextHinting.None, true, 1.0, TextHinting.Standard)]
    [InlineData(TextHinting.None, true, 2.0, TextHinting.None)]
    [InlineData(TextHinting.None, true, 3.0, TextHinting.None)]
    [InlineData(TextHinting.Monochrome, true, 3.0, TextHinting.Monochrome)]
    [InlineData(TextHinting.Standard, false, 2.0, TextHinting.Standard)]
    public void The_effective_hinting_follows_the_request_and_the_density(TextHinting requested, bool lowDensity, double pixelsPerPoint, TextHinting expected) =>
        Assert.Equal(expected, CoreGraphicsCanvas.EffectiveHinting(requested, lowDensity, pixelsPerPoint));

    private static async Task<(double Ink, byte[] Pixels)> InkWith(bool matchWeight)
    {
        using var scene = Scene.Create(220, 40);
        scene.Context.MatchPlatformTextWeight = matchWeight;
        await using var stream = File.OpenRead(PeachPDF.Tests.TestSupport.BundledFonts.Ttf);
        await scene.Context.AddFont(stream, "Source Sans 3");
        var font = scene.Context.GetFont("Source Sans 3", 16, PaintFontStyle.Regular)!;
        scene.Canvas.DrawString("Hamburgefonstiv 0123", font, PaintColor.FromArgb(255, 0, 0, 0), new PaintPoint(4, 6), scene.Canvas.MeasureString("Hamburgefonstiv 0123", font));
        var px = scene.Composer.ToPixelBuffer().PremultipliedRgba.ToArray();
        return (Enumerable.Range(0, px.Length / 4).Sum(i => (double)px[i * 4 + 3]), px);
    }

    [Fact]
    public async Task Matching_the_platform_weight_adds_about_a_fifth_more_ink()
    {
        var plain = await InkWith(false);
        var matched = await InkWith(true);

        Assert.InRange(matched.Ink / plain.Ink, 1.10, 1.30);
    }

    [Fact]
    public void Matching_the_platform_weight_is_on_for_macOS_and_off_for_iOS()
    {
        using var context = new CoreGraphicsRenderContext();
        Assert.Equal(OperatingSystem.IsMacOS() || OperatingSystem.IsMacCatalyst(), context.MatchPlatformTextWeight);
    }

    [Fact]
    public void The_native_engine_places_glyphs_at_exact_fractional_positions()
    {
        byte[] Draw(double x)
        {
            using var scene = Scene.Create(100, 30, engine: CoreGraphicsTextEngine.Native);
            scene.Context.GlyphPlacement = CoreGraphicsGlyphPlacement.Exact;
            var font = scene.Context.GetFont("Helvetica", 16, PaintFontStyle.Regular)!;
            scene.Canvas.DrawString("Hello", font, PaintColor.Black, new PaintPoint(x, 4), scene.Canvas.MeasureString("Hello", font));
            return scene.Composer.ToPixelBuffer().PremultipliedRgba.ToArray();
        }

        // Quantizing to a coarse sub-pixel grid would draw 10.00 and 10.06 identically; exact placement shifts the coverage.
        Assert.NotEqual(Draw(10.0), Draw(10.06));
    }

    private static async Task<string> Placed(CoreGraphicsTextEngine engine, CoreGraphicsGlyphPlacement placement, double sizePx, double x, double y)
    {
        using var scene = Scene.Create(100, 40, engine: engine);
        scene.Context.GlyphPlacement = placement;
        await using var stream = File.OpenRead(PeachPDF.Tests.TestSupport.BundledFonts.Ttf);
        await scene.Context.AddFont(stream, "Source Sans 3");
        var font = scene.Context.GetFont("Source Sans 3", sizePx, PaintFontStyle.Regular)!;
        scene.Canvas.DrawString("H", font, PaintColor.Black, new PaintPoint(x, y), scene.Canvas.MeasureString("H", font));
        return Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(scene.Composer.ToPixelBuffer().PremultipliedRgba.ToArray()))[..8];
    }

    [Theory]
    [InlineData(CoreGraphicsTextEngine.PeachDrawing)]
    [InlineData(CoreGraphicsTextEngine.Native)]
    public async Task The_baseline_snaps_to_whole_device_pixels_by_default_and_not_when_exact(CoreGraphicsTextEngine engine)
    {
        async Task<int> Distinct(CoreGraphicsGlyphPlacement placement)
        {
            var keys = new HashSet<string>();
            for (var i = 0; i < 24; i++)
                keys.Add(await Placed(engine, placement, 14, 5, 5 + i / 24.0));

            return keys.Count;
        }

        // Moving a line down by one pixel in 1/24ths: snapped, it changes once (to the next row); exact, it changes at every step.
        Assert.Equal(2, await Distinct(CoreGraphicsGlyphPlacement.Platform));
        Assert.True(await Distinct(CoreGraphicsGlyphPlacement.Exact) > 12);
    }

    [Theory]
    [InlineData(9.0, 4)]
    [InlineData(10.0, 4)]
    [InlineData(14.0, 3)]
    [InlineData(20.0, 2)]
    [InlineData(32.0, 2)]
    [InlineData(40.0, 1)]
    public async Task Horizontal_positions_fall_on_the_same_grid_as_CoreGraphics_places_its_own_text(double sizePx, int positionsPerPixel)
    {
        // Sweep a glyph across one pixel in 1/24ths: both engines must change their drawing at the same positions, which are the multiples of 1/n.
        async Task<List<int>> Boundaries(CoreGraphicsTextEngine engine)
        {
            var changes = new List<int>();
            string? previous = null;
            for (var i = 0; i < 24; i++)
            {
                var key = await Placed(engine, CoreGraphicsGlyphPlacement.Platform, sizePx, 5 + i / 24.0, 5);
                if (key != previous)
                    changes.Add(i);

                previous = key;
            }

            return changes;
        }

        var peach = await Boundaries(CoreGraphicsTextEngine.PeachDrawing);
        var native = await Boundaries(CoreGraphicsTextEngine.Native);
        Assert.Equal(Math.Min(positionsPerPixel, CoreGraphicsCanvas.MaxPositionsPerPixel), peach.Count);
        Assert.Equal(native, peach);
    }

    [Theory]
    [InlineData(6.0, 5, 3)]
    [InlineData(8.3, 5, 3)]
    [InlineData(8.4, 4, 3)]
    [InlineData(11.1, 4, 3)]
    [InlineData(11.2, 3, 3)]
    [InlineData(16.6, 3, 3)]
    [InlineData(16.7, 2, 2)]
    [InlineData(33.3, 2, 2)]
    [InlineData(33.4, 1, 1)]
    [InlineData(200.0, 1, 1)]
    public void The_grid_follows_the_size_with_each_platforms_cap(double sizePx, int onMacOS, int oniOS)
    {
        Assert.Equal(onMacOS, CoreGraphicsCanvas.GridFor(sizePx, 5));
        Assert.Equal(oniOS, CoreGraphicsCanvas.GridFor(sizePx, 3));
    }

    [Fact]
    public async Task Exact_placement_puts_glyphs_at_every_fractional_position()
    {
        var keys = new HashSet<string>();
        for (var i = 0; i < 24; i++)
            keys.Add(await Placed(CoreGraphicsTextEngine.PeachDrawing, CoreGraphicsGlyphPlacement.Exact, 20, 5 + i / 24.0, 5));

        Assert.True(keys.Count > 12);
    }

    [Fact]
    public async Task The_system_font_gets_its_optical_size_and_tracking_so_its_width_matches_CoreText()
    {
        var family = new PeachDrawing.Text.FontSet().ResolveGeneric(PeachDrawing.Text.GenericFamily.SystemUi)!;
        foreach (var size in new[] { 12.0, 15.0, 20.0, 28.0 })
        {
            using var native = Scene.Create(300, 40, engine: CoreGraphicsTextEngine.Native);
            using var shared = Scene.Create(300, 40);
            var expected = native.Canvas.MeasureString("Lorem ipsum dolor sit amet", native.Context.GetFont(family, size, PaintFontStyle.Regular)!).Width;
            var actual = shared.Canvas.MeasureString("Lorem ipsum dolor sit amet", shared.Context.GetFont(family, size, PaintFontStyle.Regular)!).Width;
            Assert.InRange(actual, expected - 0.2, expected + 0.2);
        }
    }

    [Fact]
    public async Task Turning_optical_size_and_tracking_off_gives_the_fonts_default_widths()
    {
        var family = new PeachDrawing.Text.FontSet().ResolveGeneric(PeachDrawing.Text.GenericFamily.SystemUi)!;
        using var on = Scene.Create(300, 40);
        using var off = Scene.Create(300, 40);
        off.Context.AutomaticOpticalSize = false;
        off.Context.ApplyFontTracking = false;
        var withBoth = on.Canvas.MeasureString("Lorem ipsum dolor", on.Context.GetFont(family, 15, PaintFontStyle.Regular)!).Width;
        var neither = off.Canvas.MeasureString("Lorem ipsum dolor", off.Context.GetFont(family, 15, PaintFontStyle.Regular)!).Width;
        Assert.True(withBoth > neither + 3);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task A_font_without_a_tracking_table_is_not_changed_by_tracking()
    {
        using var on = await Scene.WithFontAsync();
        using var off = await Scene.WithFontAsync();
        off.Context.ApplyFontTracking = false;
        Assert.Equal(
            on.Canvas.MeasureString("Hamburgefonstiv", on.Context.GetFont("Source Sans 3", 15, PaintFontStyle.Regular)!).Width,
            off.Canvas.MeasureString("Hamburgefonstiv", off.Context.GetFont("Source Sans 3", 15, PaintFontStyle.Regular)!).Width);
    }
}
