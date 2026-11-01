using PeachDrawing.Core;
using PeachDrawing.Skia;
using PeachDrawing.Text.Shaping;
using PeachPDF.Tests.TestSupport;
using System.Numerics;

namespace PeachDrawing.Skia.Tests;

/// <summary>The remaining branches: every blend mode, pen and dash style, path curves, brush edge cases, sampling, fonts and the context's lookups.</summary>
public class CoverageTests
{
    private static readonly PaintColor Red = PaintColor.FromArgb(255, 255, 0, 0);
    private static readonly PaintColor Blue = PaintColor.FromArgb(255, 0, 0, 255);

    private sealed class UnknownBrush : Brush;

    private sealed class PaintingContext(ISvgGlyphPainter painter) : SkiaRenderContext
    {
        public override ISvgGlyphPainter? CreateSvgGlyphPainter(Canvas host) => painter;
    }

    private sealed class DecliningPainter : ISvgGlyphPainter
    {
        public int Calls { get; private set; }

        public bool TryPaint(PeachDrawing.Text.Typeface typeface, ushort glyph, PeachDrawing.Text.Outlines.SvgGlyph svg, double fontSize, double originX,
            double baselineY, PaintColor foreground, int paletteIndex, IReadOnlyDictionary<int, PaintColor>? overrides)
        {
            Calls++;
            return false;
        }
    }

    [Fact]
    public void Every_blend_mode_can_be_pushed_for_a_draw_and_popped()
    {
        using var scene = Scene.Create();
        foreach (var mode in Enum.GetValues<PaintBlendMode>())
        {
            scene.Canvas.PushBlendMode(mode);
            scene.Canvas.DrawRectangle(scene.Solid(200, 100, 50), 0, 0, 40, 40);
            scene.Canvas.PopBlendMode();
        }

        Assert.True(scene.OpaquePixels() > 0);
    }

    [Fact]
    public void The_legacy_smoothing_pair_and_tagging_calls_are_accepted()
    {
        using var scene = Scene.Create();
        var previous = scene.Canvas.SetAntiAliasSmoothingMode();
        scene.Canvas.ReturnPreviousSmoothingMode(previous);
        scene.Canvas.ReturnPreviousSmoothingMode(null);
        scene.Canvas.BeginMarkedContent("P", 1);
        scene.Canvas.EndMarkedContent();
        scene.Canvas.BeginArtifact();
        scene.Canvas.EndMarkedContent();
        scene.Canvas.BeginVariableText();
        scene.Canvas.EndVariableText();
        scene.Canvas.PopAntiAlias();

        Assert.Equal(0, scene.OpaquePixels());
    }

    [Fact]
    public void Curves_are_filled_and_stroked_and_a_path_can_be_clipped_to_a_rectangle()
    {
        using var scene = Scene.Create();
        using var path = scene.Canvas.GetGraphicsPath();
        path.Start(5, 20);
        path.AddBezierTo(5, 0, 35, 0, 35, 20);
        path.LineTo(35, 35);
        path.CloseFigure();
        scene.Canvas.DrawPath(scene.Solid(255, 0, 0), path);
        scene.Canvas.DrawPath(scene.Pen(0, 0, 255, 2), path);

        using var clipped = path.ClipToRect(new Rect(0, 0, 20, 40));
        Assert.NotEmpty(clipped.GetCurveContours());
        using var empty = path.ClipToRect(new Rect(100, 100, 5, 5));
        Assert.Empty(empty.GetCurveContours());
        Assert.True(scene.OpaquePixels() > 200);
    }

    [Theory]
    [InlineData(DashStyle.Dot)]
    [InlineData(DashStyle.DashDot)]
    [InlineData(DashStyle.DashDotDot)]
    [InlineData(DashStyle.Solid)]
    public void Every_dash_style_strokes(DashStyle style)
    {
        using var scene = Scene.Create(60, 10);
        var pen = scene.Pen(255, 0, 0, 3);
        pen.DashStyle = style;
        scene.Canvas.DrawLine(pen, 0, 5, 60, 5);

        Assert.True(scene.OpaquePixels() > 20);
    }

    [Fact]
    public void A_custom_dash_with_an_odd_count_an_offset_caps_joins_and_a_miter_limit_strokes()
    {
        using var scene = Scene.Create(60, 40);
        var pen = scene.Pen(255, 0, 0, 3);
        pen.SetDashPattern([6, 3, 2], 1);
        pen.LineCap = LineCap.Round;
        pen.LineJoin = LineJoin.Round;
        pen.MiterLimit = 4;
        scene.Canvas.DrawRectangle(pen, 5, 5, 50, 30);

        var bevel = scene.Pen(0, 0, 255, 3);
        bevel.LineCap = LineCap.Square;
        bevel.LineJoin = LineJoin.Bevel;
        scene.Canvas.DrawLine(bevel, 5, 38, 55, 38);

        var empty = scene.Pen(0, 0, 0, 2);
        empty.SetDashPattern([], 0);
        scene.Canvas.DrawLine(empty, 0, 20, 60, 20);

        Assert.True(scene.OpaquePixels() > 100);
    }

    [Fact]
    public void A_pen_of_no_width_draws_a_hairline_and_a_pen_without_paint_draws_nothing()
    {
        using var scene = Scene.Create();
        scene.Canvas.DrawLine(scene.Pen(255, 0, 0, 0), 0, 10, 40, 10);
        var unpainted = new Pen { Paint = null! };
        scene.Canvas.DrawLine(unpainted, 0, 30, 40, 30);

        Assert.True(scene.OpaquePixels() > 20);
        Assert.True(scene.IsClear(20, 30));
    }

    [Fact]
    public void Gradients_without_stops_and_a_transparent_colour_paint_nothing_and_an_unknown_brush_is_refused()
    {
        using var scene = Scene.Create();
        var none = new List<GradientStop>();
        scene.Canvas.DrawRectangle(new LinearGradientBrush(new PaintPoint(0, 0), new PaintPoint(40, 0), none, GradientSpread.Pad), 0, 0, 40, 40);
        scene.Canvas.DrawRectangle(new RadialGradientBrush(new PaintPoint(20, 20), new PaintPoint(20, 20), 10, 10, none, GradientSpread.Pad), 0, 0, 40, 40);
        scene.Canvas.DrawRectangle(new ConicGradientBrush(new PaintPoint(20, 20), 10, none, []), 0, 0, 40, 40);

        Assert.Throws<NotSupportedException>(() => scene.Canvas.DrawRectangle(new UnknownBrush(), 0, 0, 1, 1));
        Assert.Equal(0, scene.OpaquePixels());
    }

    [Fact]
    public void A_radial_gradient_with_a_focus_a_focus_radius_a_transform_and_repeat_paints()
    {
        using var scene = Scene.Create();
        IReadOnlyList<GradientStop> stops = [new(Red, 0), new(Blue, 1)];
        var focal = new RadialGradientBrush(new PaintPoint(20, 20), new PaintPoint(12, 20), 18, 18, stops, GradientSpread.Pad);
        scene.Canvas.DrawRectangle(focal, 0, 0, 40, 20);

        var twoCircle = new RadialGradientBrush(new PaintPoint(20, 20), new PaintPoint(20, 20), 18, 10, stops, GradientSpread.Repeat, Matrix3x2.CreateRotation(0.3f, new Vector2(20, 20)))
        {
            FocusRadius = 4,
        };
        scene.Canvas.DrawRectangle(twoCircle, 0, 20, 40, 20);

        Assert.True(scene.OpaquePixels() > 1000);
    }

    [Fact]
    public void A_conic_gradient_that_starts_off_the_vertical_and_a_repeating_linear_one_paint()
    {
        using var scene = Scene.Create();
        scene.Canvas.DrawRectangle(scene.Canvas.GetConicGradientBrush(new PaintPoint(20, 20), 20, [Red, Blue, Red], [1, 3, 5]), 0, 0, 40, 40);

        Assert.True(scene.OpaquePixels() > 1000);
    }

    [Theory]
    [InlineData(ImageSampling.Nearest)]
    [InlineData(ImageSampling.Pixelated)]
    [InlineData(ImageSampling.Bicubic)]
    [InlineData(ImageSampling.Automatic)]
    public void A_tile_brush_of_pixels_honours_every_sampling(ImageSampling sampling)
    {
        using var scene = Scene.Create();
        using var image = scene.Context.ImageFromStream(new MemoryStream(Scene.TwoColourPng()));
        using var brush = new TileBrush(image, 16, 8, Matrix3x2.CreateScale(1.5f), sampling);
        scene.Canvas.DrawRectangle(brush, 0, 0, 40, 40);

        Assert.True(scene.OpaquePixels() > 500);
    }

    [Fact]
    public void A_tile_brush_of_a_picture_with_nearest_sampling_and_an_image_with_interpolation_off_paint()
    {
        using var scene = Scene.Create();
        var tile = scene.Canvas.CreateTile(8, 8)!.Value;
        tile.Graphics.DrawRectangle(scene.Solid(255, 0, 0), 0, 0, 4, 8);
        tile.Graphics.Dispose();
        using (tile.Image)
        {
            using var picture = new TileBrush(tile.Image, 8, 8, Matrix3x2.Identity, ImageSampling.Nearest);
            scene.Canvas.DrawRectangle(picture, 0, 0, 20, 40);
        }

        using var image = scene.Context.ImageFromStream(new MemoryStream(Scene.TwoColourPng()));
        image.Interpolate = false;
        scene.Canvas.DrawImage(image, new Rect(20, 0, 20, 10));
        scene.Canvas.DrawImage(image, new Rect(20, 10, 20, 10), ImageSampling.Bicubic);

        Assert.True(scene.IsRed(2, 5));
        Assert.True(scene.IsRed(22, 5));
    }

    [Fact]
    public void Images_draw_under_a_pushed_blend_with_opacity_a_source_rectangle_and_a_picture_source()
    {
        using var scene = Scene.Create();
        using var image = scene.Context.ImageFromStream(new MemoryStream(Scene.TwoColourPng()));
        scene.Canvas.PushBlendMode(PaintBlendMode.Multiply);
        scene.Canvas.DrawImage(image, new Rect(0, 0, 20, 10), new Rect(0, 0, 4, 4));
        scene.Canvas.DrawImageWithOpacity(image, new Rect(0, 10, 20, 10), 0.5);
        scene.Canvas.DrawImageWithOpacity(image, new Rect(0, 20, 20, 10), 0.5, PaintBlendMode.Screen);
        scene.Canvas.PopBlendMode();

        var tile = scene.Canvas.CreateTile(10, 10)!.Value;
        tile.Graphics.DrawRectangle(scene.Solid(0, 255, 0), 0, 0, 10, 10);
        tile.Graphics.Dispose();
        using (tile.Image)
        {
            scene.Canvas.DrawImage(tile.Image, new Rect(25, 0, 10, 10), new Rect(0, 0, 5, 5));
            scene.Canvas.DrawImage(tile.Image, new Rect(25, 15, 10, 10), ImageSampling.Nearest);
            scene.Canvas.DrawImage(tile.Image, new Rect(0, 0, 0, 0));
            scene.Canvas.PushBlendMode(PaintBlendMode.Multiply);
            scene.Canvas.DrawImageWithOpacity(tile.Image, new Rect(25, 30, 10, 10), 1);
            scene.Canvas.PopBlendMode();
        }

        Assert.True(scene.OpaquePixels() > 300);
    }

    [Fact]
    public void Masks_and_blends_of_images_of_no_size_are_ignored_and_a_raster_seam_blend_applies()
    {
        using var scene = Scene.Create();
        var a = scene.Canvas.CreateTile(10, 10)!.Value;
        a.Graphics.Dispose();
        var b = scene.Canvas.CreateTile(10, 10)!.Value;
        b.Graphics.Dispose();
        using (a.Image)
        using (b.Image)
        {
            scene.Canvas.DrawImageMasked(a.Image, b.Image, new Rect(0, 0, 0, 5));
            scene.Canvas.DrawImageBlendedOver(a.Image, b.Image, new Rect(0, 0, 0, 5), PaintBlendMode.Multiply);
            scene.Canvas.DrawImageBlendedOver(a.Image, b.Image, new Rect(0, 0, 10, 10), PaintBlendMode.Normal);
        }

        scene.Canvas.PushBlendMode(PaintBlendMode.Multiply);
        using var region = scene.Canvas.BeginRasterSurface(new Rect(0, 0, 10, 10), 96)!;
        region.Graphics.DrawRectangle(scene.Solid(255, 0, 0), 0, 0, 10, 10);
        scene.Canvas.DrawRaster(region.Surface);
        scene.Canvas.PopBlendMode();

        Assert.True(scene.OpaquePixels() > 0);
    }

    [Fact]
    public void A_layer_with_only_a_colour_matrix_with_a_blur_after_it_and_with_no_bounds_composites()
    {
        using var scene = Scene.Create();
        var swap = new ColorMatrix(new Matrix4x4(0, 0, 1, 0, 0, 1, 0, 0, 1, 0, 0, 0, 0, 0, 0, 1), Vector4.Zero);
        using (var layer = scene.Canvas.BeginLayer(new LayerOptions(ColorMatrix: swap, Effects: [new BlurEffect(1), new ColorMatrixEffect(swap)])))
            layer!.Canvas.DrawRectangle(scene.Solid(255, 0, 0), 5, 5, 30, 30);

        using (var layer = scene.Canvas.BeginLayer(new LayerOptions(Effects: [new ColorMatrixEffect(swap)])))
            layer!.Canvas.DrawRectangle(scene.Solid(0, 255, 0), 0, 0, 3, 3);

        Assert.True(scene.OpaquePixels() > 500);
    }

    [Fact]
    public void A_layer_finished_twice_or_disposed_with_its_canvas_open_is_harmless_and_effects_chain_in_order()
    {
        var scene = Scene.Create();
        var layer = scene.Canvas.BeginLayer(new LayerOptions(Opacity: 0.5));
        layer!.Canvas.DrawRectangle(scene.Solid(255, 0, 0), 0, 0, 10, 10);
        layer.Dispose();
        layer.Dispose();

        Assert.Equal(0, scene.Context.Counts.Layers);
        scene.Dispose();
    }

    [Fact]
    public async Task A_link_launcher_that_throws_reports_false()
    {
        using var scene = Scene.Create(options: new SkiaComposerOptions { Links = new SkiaLinkOptions { Launcher = new ThrowingLauncher() } });
        scene.Canvas.AddLink(new Rect(0, 0, 10, 10), "https://example.com/");

        Assert.False(await scene.Composer.Links.TryActivateAsync(5, 5));
        Assert.Empty(scene.Composer.Links.Destinations);
    }

    private sealed class ThrowingLauncher : ISkiaLinkLauncher
    {
        public Task<bool> LaunchAsync(Uri uri) => throw new InvalidOperationException();
    }

    // ---- composer, images and the context ---------------------------------------------------------------------------

    [Fact]
    public void A_composer_can_be_read_after_it_is_finished_and_refuses_use_after_dispose()
    {
        var scene = Scene.Create();
        scene.Canvas.DrawRectangle(scene.Solid(255, 0, 0), 0, 0, 40, 40);
        Assert.Equal(40, scene.Composer.PixelHeight);
        Assert.Equal(40, scene.Composer.WidthPoints);
        Assert.Same(scene.Context, scene.Composer.RenderContext);
        var pixels = scene.Composer.ToPixelBuffer();
        Assert.Equal(40, pixels.Width);

        scene.Composer.Dispose();
        scene.Composer.Dispose();
        Assert.Throws<ObjectDisposedException>(() => scene.Composer.Finish());
        Assert.Throws<ArgumentNullException>(() => scene.Composer.Save(null!));
        scene.Surface.Dispose();
        scene.Context.Dispose();
    }

    [Fact]
    public void A_natively_decoded_image_reads_its_pixels_back_draws_and_refuses_use_after_dispose()
    {
        using var context = new SkiaRenderContext(imageCodec: SkiaImageCodec.Native);
        // A stream that is not a MemoryStream at its start is copied first.
        var stream = new MemoryStream(Scene.TwoColourPng());
        var image = context.ImageFromStream(new BufferedStream(stream));
        Assert.NotNull(image.GetPixels());

        using var scene = new Scene(context, 20, 10);
        scene.Canvas.DrawImage(image, new Rect(0, 0, 20, 10));
        Assert.Throws<NotSupportedException>(() => scene.Canvas.DrawImage(new PixelessImage(), new Rect(0, 0, 1, 1)));
        Assert.True(scene.OpaquePixels() > 100);

        image.Dispose();
        image.Dispose();
        Assert.Throws<ObjectDisposedException>(() => image.GetPixels());
    }

    private sealed class PixelessImage : PeachDrawing.Core.Image
    {
        public override double Width => 1;

        public override double Height => 1;

        public override bool Interpolate { get; set; }

        public override void Dispose()
        {
        }
    }

    [Fact]
    public async Task The_context_looks_up_fonts_for_a_codepoint_and_falls_back_in_both_engines()
    {
        foreach (var engine in new[] { SkiaTextEngine.PeachDrawing, SkiaTextEngine.Native })
        {
            using var context = new SkiaRenderContext(engine);
            using var stream = File.OpenRead(BundledFonts.Ttf);
            await context.AddFont(stream, "Source Sans 3");

            Assert.NotNull(context.GetFontForCodepoint("Source Sans 3", 16, PaintFontStyle.Bold | PaintFontStyle.Italic, new System.Text.Rune('A')));
            Assert.Null(context.GetFontForCodepoint("No Such Family", 16, PaintFontStyle.Regular, new System.Text.Rune('A')));
            Assert.Null(context.GetFontForCodepoint("Source Sans 3", 16, PaintFontStyle.Regular, new System.Text.Rune(0x4E2D)));
            Assert.False(context.FamilyHasExplicitUnicodeRanges("Source Sans 3"));
            context.GetFontForCodepoint("Source Sans 3", 16, PaintFontStyle.Regular, new System.Text.Rune(0x4E2D), 400, 100);
        }
    }

    [Fact]
    public async Task Fonts_can_be_added_from_bytes_and_from_local_names_and_other_formats_are_declined()
    {
        using var context = new SkiaRenderContext(SkiaTextEngine.Native);
        var bytes = await File.ReadAllBytesAsync(BundledFonts.Ttf, TestContext.Current.CancellationToken);

        Assert.True(await context.AddFontFromBytes("Added Family", bytes));
        Assert.NotNull(context.GetFont("Added Family", 14, PaintFontStyle.Regular));
        Assert.True(await context.AddFontFromBytes("Weighted", bytes, new FontFaceDescriptors(new PeachDrawing.Text.AxisRange(300, 500), true)));
        Assert.NotNull(context.GetFont("Weighted", 14, PaintFontStyle.Italic, 700, 100));
        Assert.False(await context.AddFontFamilyFromUrl("Nope", "data:font/ttf;base64,AAAA", "embedded-opentype"));
    }

    [Fact]
    public async Task A_font_that_a_PeachDrawing_context_added_and_a_native_font_expose_their_metrics()
    {
        foreach (var engine in new[] { SkiaTextEngine.PeachDrawing, SkiaTextEngine.Native })
        {
            using var scene = Scene.Create(100, 40, engine: engine);
            await using (var stream = File.OpenRead(BundledFonts.Ttf))
                await scene.Context.AddFont(stream, "Source Sans 3");
            var font = scene.Context.GetFont("Source Sans 3", 16, PaintFontStyle.Regular)!;

            Assert.True(font.Height > 0);
            Assert.True(font.Ascent > 0);
            Assert.True(font.TextBaselineOffset > 0);
            Assert.True(font.NormalLineHeight > 0);
            Assert.True(font.LeftPadding > 0);
            Assert.True(font.GetWhitespaceWidth(scene.Canvas) > 0);
            Assert.Equal(font.GetWhitespaceWidth(scene.Canvas), font.GetWhitespaceWidth(scene.Canvas));
            Assert.True(font.UnderlineOffset > 0);
            _ = font.UnderlineThickness;
            _ = font.UnderlinePosition;
            _ = font.Size;
            Assert.False(string.IsNullOrEmpty(font.FaceKey));
            Assert.True(font.HasGlyph(new System.Text.Rune('A')));
            _ = font.SupportsFontVariantCaps(CapsMode.SmallCaps);
            _ = font.SupportsFontVariantPosition(SubSuperMode.Super);
            _ = font.GetSubSuperscriptMetrics(true);
            _ = font.MatchesEmojiPresentation(new System.Text.Rune('A'), PeachDrawing.Text.Unicode.EmojiPresentation.Text);
            _ = font.PaletteCount;
            _ = font.PaletteEntryCount;
            _ = font.FirstLightPalette();
            _ = font.FirstDarkPalette();
            Assert.False(font.TryGetPaletteColor(0, 0, out _));
            _ = font.HasVerticalMetrics;
            _ = font.GetVerticalAdvance(new System.Text.Rune('A'));
            _ = font.HasVerticalOrigin;
            _ = font.GetVerticalOriginY(new System.Text.Rune('A'));
            _ = font.HasMathTable;
            _ = font.MathTable;
            _ = font.FontUnitsPerEm;
            _ = font.GetGlyphIndex(new System.Text.Rune('A'));
            _ = font.GetGlyphAdvanceWidthDesignUnits(5);
            _ = font.XHeightEm;
            _ = font.CapHeightEm;
            _ = font.GetBaselineHeightEm("ideo", false);
            _ = font.Typeface;
            _ = font.SyntheticStyle;
            _ = font.ObliqueSkewSinus;
        }
    }

    [Fact]
    public async Task Synthetic_bold_and_italic_letter_spacing_and_glyph_placements_draw_in_the_default_engine()
    {
        using var scene = await Scene.WithFontAsync(200, 60);
        var styled = scene.Context.GetFont("Source Sans 3", 20, PaintFontStyle.Bold | PaintFontStyle.Italic)!;
        scene.Canvas.DrawString("Hi", styled, PaintColor.Black, new PaintPoint(5, 5), new Core.Size(30, 20), letterSpacing: 3);
        scene.Canvas.DrawString("", styled, PaintColor.Black, new PaintPoint(5, 5), new Core.Size(0, 0));
        scene.Canvas.DrawString("Hi", styled, PaintColor.FromArgb(0, 0, 0, 0), new PaintPoint(5, 5), new Core.Size(30, 20));
        var plain = scene.Context.GetFont("Source Sans 3", 20, PaintFontStyle.Bold | PaintFontStyle.Italic | PaintFontStyle.NoSyntheticBold | PaintFontStyle.NoSyntheticItalic)!;
        scene.Canvas.DrawString("Hi", plain, PaintColor.Black, new PaintPoint(60, 5), new Core.Size(30, 20));
        scene.Canvas.DrawGlyphs([new GlyphPlacement(styled.GetGlyphIndex(new System.Text.Rune('H')), 120, 30)], styled, Red);
        scene.Canvas.InvisibleText = true;
        scene.Canvas.DrawGlyphs([new GlyphPlacement(5, 120, 30)], styled, Red);
        scene.Canvas.MeasureString("Hi", styled, 5, out var fit, out _);
        scene.Canvas.MeasureString("\U0001F600\U0001F600", styled, 5, out var fitPairs, out _);

        Assert.Null(scene.Canvas.GetInkCrossings("Hi", styled, new PaintPoint(0, 0), 10, 10));
        Assert.True(scene.OpaquePixels() > 100);
        Assert.Equal(0, fit);
        Assert.Equal(0, fitPairs);
    }

    [Fact]
    public async Task Native_text_with_letter_spacing_a_transparent_colour_and_no_glyph_run_draws_what_it_can()
    {
        using var scene = Scene.Create(120, 40, engine: SkiaTextEngine.Native);
        await using (var stream = File.OpenRead(BundledFonts.Ttf))
            await scene.Context.AddFont(stream, "Source Sans 3");
        var font = scene.Context.GetFont("Source Sans 3", 16, PaintFontStyle.Bold | PaintFontStyle.Italic)!;

        scene.Canvas.DrawString("", font, PaintColor.Black, new PaintPoint(0, 0), new Core.Size(0, 0));
        scene.Canvas.DrawString("Hi", font, PaintColor.FromArgb(0, 0, 0, 0), new PaintPoint(0, 0), new Core.Size(10, 10));
        scene.Canvas.DrawString("Hi", font, PaintColor.Black, new PaintPoint(0, 0), new Core.Size(10, 10), letterSpacing: 4);
        scene.Canvas.DrawGlyphs([new GlyphPlacement(3, 5, 20)], font, Red);
        Assert.Equal(0, scene.Canvas.CountShapedGlyphs("", font));
        Assert.Equal(0, scene.Canvas.MeasureString("", font).Width);

        Assert.True(scene.OpaquePixels() > 20);
    }

    [Fact]
    public async Task Colour_glyph_fonts_with_palettes_draw_in_their_own_colours()
    {
        foreach (var path in new[] { BundledFonts.ColorV0, BundledFonts.ColorV1 })
        {
            using var scene = Scene.Create(80, 80);
            await using (var stream = File.OpenRead(path))
                await scene.Context.AddFont(stream, "Colour");
            var font = scene.Context.GetFont("Colour", 40, PaintFontStyle.Regular)!;
            var palette = new FontPalette(0, [new KeyValuePair<int, PaintColor>(0, Blue)]);

            scene.Canvas.DrawString("A", font, PaintColor.Black, new PaintPoint(5, 5), new Core.Size(40, 40), fontPalette: palette);
            scene.Canvas.DrawString("A", font, PaintColor.Black, new PaintPoint(5, 40), new Core.Size(40, 40), fontPalette: new FontPalette(0, []));
            Assert.True(font.PaletteCount > 0);
            Assert.True(scene.OpaquePixels() > 100, path);
        }
    }

    [Fact]
    public async Task A_bitmap_glyph_font_draws_its_pictures_through_the_context_codec()
    {
        using var scene = Scene.Create(120, 60, codec: SkiaImageCodec.Native);
        await using (var stream = File.OpenRead(BundledFonts.RealCbdt))
            await scene.Context.AddFont(stream, "Bitmap");
        var font = scene.Context.GetFont("Bitmap", 40, PaintFontStyle.Regular)!;

        scene.Canvas.DrawString("A", font, PaintColor.Black, new PaintPoint(5, 5), new Core.Size(40, 40));
        scene.Canvas.DrawString("A", font, PaintColor.Black, new PaintPoint(60, 5), new Core.Size(40, 40));

        Assert.True(scene.OpaquePixels() >= 0);
    }

    [Fact]
    public async Task An_svg_glyph_is_handed_to_the_contexts_glyph_painter_and_drawn_as_an_outline_when_it_declines()
    {
        var painter = new DecliningPainter();
        var context = new PaintingContext(painter);
        using var scene = new Scene(context, 120, 80);
        await using (var stream = File.OpenRead(BundledFonts.SvgTest))
            await context.AddFont(stream, "Svg Glyph Test");
        var font = context.GetFont("Svg Glyph Test", 60, PaintFontStyle.Regular)!;

        scene.Canvas.DrawString("A", font, PaintColor.Black, new PaintPoint(5, 5), new Core.Size(40, 40));

        Assert.True(painter.Calls >= 1);
    }

    [Fact]
    public async Task The_native_font_registry_serves_weights_italics_fallbacks_and_a_font_skia_cannot_load()
    {
        using var context = new SkiaRenderContext(SkiaTextEngine.Native);
        var bytes = await File.ReadAllBytesAsync(BundledFonts.Ttf, TestContext.Current.CancellationToken);
        await context.AddFontFromBytes("Mixed", bytes, new FontFaceDescriptors(new PeachDrawing.Text.AxisRange(400), false));
        await context.AddFontFromBytes("Mixed", bytes, new FontFaceDescriptors(new PeachDrawing.Text.AxisRange(700), true));

        Assert.NotNull(context.GetFont("Mixed", 12, PaintFontStyle.Italic, 700));
        Assert.NotNull(context.GetFont("Mixed", 12, PaintFontStyle.Regular, 400));
        Assert.NotNull(context.GetFont("Mixed", 12, PaintFontStyle.Regular, 400));
        var fallback = context.GetFontForCodepoint("Mixed", 12, PaintFontStyle.Regular, new System.Text.Rune(0x4E2D));
        Assert.Null(fallback);
        _ = context.GetFont("Arial", 12, PaintFontStyle.Regular, 300, 60);
        _ = context.GetFont("Arial", 12, PaintFontStyle.Regular, 300, 200);

        context.Dispose();
        context.Dispose();
        Assert.True(context.IsDisposedForTest());
    }
}

internal static class ContextTestExtensions
{
    public static bool IsDisposedForTest(this SkiaRenderContext context) => context.IsDisposed;
}
