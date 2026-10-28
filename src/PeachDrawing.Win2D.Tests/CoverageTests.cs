using PeachDrawing.Core;
using PeachDrawing.Text;
using PeachDrawing.Text.Shaping;
using PeachDrawing.Win2D;
using PeachPDF.Tests.TestSupport;
using System.Numerics;

namespace PeachDrawing.Win2D.Tests;

/// <summary>The less common branches of the canvas: every pen style, curves, glyph kinds, image calls and the file formats.</summary>
public class CoverageTests
{
    private static GraphicsPath Curve(Canvas canvas)
    {
        var path = canvas.GetGraphicsPath();
        path.Start(2, 2);
        path.AddBezierTo(20, 2, 20, 20, 2, 20);
        path.LineTo(2, 2);
        path.CloseFigure();
        return path;
    }

    [Fact]
    public void Curves_are_filled_stroked_and_used_as_clips()
    {
        using var scene = Scene.Create();
        using var path = Curve(scene.Canvas);
        scene.Canvas.DrawPath(scene.Solid(255, 0, 0), path);
        scene.Canvas.DrawPath(scene.Pen(0, 0, 255, 2), path);
        scene.Canvas.PushClip(path);
        scene.Canvas.DrawRectangle(scene.Solid(0, 255, 0), 0, 0, 40, 40);
        scene.Canvas.PopClip();
        scene.Canvas.DrawRectangle(scene.Pen(0, 0, 0, 1), 25, 25, 10, 10);
        scene.Canvas.DrawRectangle(scene.Pen(0, 0, 0, 0), 28, 28, 4, 4);

        Assert.True(scene.OpaquePixels() > 200);
    }

    [Theory]
    [InlineData(DashStyle.Dash, 1.0, LineCap.Butt, LineJoin.Miter)]
    [InlineData(DashStyle.Dash, 3.0, LineCap.Round, LineJoin.Round)]
    [InlineData(DashStyle.Dot, 2.0, LineCap.Square, LineJoin.Bevel)]
    [InlineData(DashStyle.DashDot, 2.0, LineCap.Butt, LineJoin.Miter)]
    [InlineData(DashStyle.DashDotDot, 2.0, LineCap.Butt, LineJoin.Miter)]
    [InlineData(DashStyle.Solid, 2.0, LineCap.Butt, LineJoin.Miter)]
    public void Every_pen_style_draws(DashStyle dash, double width, LineCap cap, LineJoin join)
    {
        using var scene = Scene.Create(60, 30);
        var pen = scene.Pen(255, 0, 0, width);
        pen.DashStyle = dash;
        pen.LineCap = cap;
        pen.LineJoin = join;
        pen.MiterLimit = 4;
        scene.Canvas.DrawLine(pen, 2, 15, 58, 15);

        Assert.True(scene.OpaquePixels() > 10);
    }

    [Fact]
    public void A_custom_dash_pattern_with_an_odd_count_is_padded_and_offset()
    {
        using var scene = Scene.Create(60, 30);
        var pen = scene.Pen(255, 0, 0, 2);
        pen.SetDashPattern([4, 2, 1], 3);
        scene.Canvas.DrawLine(pen, 2, 15, 58, 15);

        var empty = scene.Pen(255, 0, 0, 2);
        empty.DashStyle = DashStyle.Custom;
        scene.Canvas.DrawLine(empty, 2, 5, 58, 5);

        Assert.True(scene.OpaquePixels() > 10);
    }

    [Fact]
    public void A_pen_painted_with_a_gradient_hatch_tile_or_conic_brush_strokes()
    {
        using var scene = Scene.Create(80, 40);
        var gradient = scene.Context.GetLinearGradientBrush(new PaintPoint(0, 0), new PaintPoint(80, 0),
            [(PaintColor.FromArgb(255, 255, 0, 0), 0), (PaintColor.FromArgb(255, 0, 0, 255), 1)]);
        var pen = scene.Context.GetPen(gradient);
        pen.Width = 4;
        scene.Canvas.DrawLine(pen, 2, 6, 78, 6);

        var conic = scene.Context.GetConicGradientBrush(new PaintPoint(40, 20), 40,
            [PaintColor.FromArgb(255, 255, 0, 0), PaintColor.FromArgb(255, 0, 0, 255)], [0, 2 * Math.PI]);
        var conicPen = scene.Context.GetPen(conic);
        conicPen.Width = 4;
        scene.Canvas.DrawLine(conicPen, 2, 14, 78, 14);

        var hatchPen = scene.Context.GetPen(new HatchBrush(HatchStyle.Cross, PaintColor.FromArgb(255, 255, 0, 0), PaintColor.White));
        hatchPen.Width = 4;
        scene.Canvas.DrawLine(hatchPen, 2, 22, 78, 22);

        var (tile, image) = scene.Canvas.CreateTile(4, 4)!.Value;
        tile.DrawRectangle(scene.Solid(255, 0, 0), 0, 0, 2, 2);
        tile.Dispose();
        var tilePen = scene.Context.GetPen(new TileBrush(image, 4, 4));
        tilePen.Width = 4;
        scene.Canvas.DrawLine(tilePen, 2, 30, 78, 30);
        image.Dispose();

        // A pen with no paint draws nothing; a transparent solid brush draws nothing either.
        scene.Canvas.DrawLine(new Pen(), 0, 0, 10, 10);
        scene.Canvas.DrawRectangle(scene.Solid(1, 2, 3, 0), 0, 0, 5, 5);

        Assert.True(scene.OpaquePixels() > 200);
        Assert.Equal(0, scene.Context.Counts.Total);
    }

    [Fact]
    public void Empty_gradients_and_a_conic_without_stops_draw_nothing()
    {
        using var scene = Scene.Create();
        scene.Canvas.DrawRectangle(new LinearGradientBrush(new PaintPoint(0, 0), new PaintPoint(10, 0), [], GradientSpread.Pad), 0, 0, 10, 10);
        scene.Canvas.DrawRectangle(new RadialGradientBrush(new PaintPoint(5, 5), new PaintPoint(5, 5), 5, 5, [], GradientSpread.Pad), 0, 0, 10, 10);
        scene.Canvas.DrawRectangle(new ConicGradientBrush(new PaintPoint(5, 5), 5, [], []), 0, 0, 10, 10);

        Assert.Equal(0, scene.OpaquePixels());
    }

    [Fact]
    public void Repeating_gradients_a_focus_and_an_unknown_brush()
    {
        using var scene = Scene.Create();
        var stops = new[] { (PaintColor.FromArgb(255, 255, 0, 0), 0.0), (PaintColor.FromArgb(255, 0, 0, 255), 0.5), (PaintColor.FromArgb(255, 0, 255, 0), 0.4) };
        scene.Canvas.DrawRectangle(scene.Context.GetLinearGradientBrush(new PaintPoint(0, 0), new PaintPoint(10, 0), stops, true), 0, 0, 20, 20);
        scene.Canvas.DrawRectangle(scene.Context.GetRadialGradientBrush(new PaintPoint(30, 10), 8, 4, stops, true, new PaintPoint(28, 10), Matrix3x2.CreateRotation(0.3f)), 20, 0, 20, 20);
        scene.Canvas.DrawRectangle(scene.Context.GetRadialGradientBrush(new PaintPoint(10, 30), 8, 8, stops, false, new PaintPoint(8, 30), 2), 0, 20, 20, 20);

        Assert.Throws<NotSupportedException>(() => scene.Canvas.DrawRectangle(new UnknownBrush(), 0, 0, 5, 5));
        Assert.True(scene.OpaquePixels() > 200);
    }

    private sealed class UnknownBrush : Brush;

    [Fact]
    public void Anti_aliasing_can_be_switched_and_the_state_is_restored()
    {
        using var scene = Scene.Create();
        var previous = scene.Canvas.SetAntiAliasSmoothingMode();
        scene.Canvas.ReturnPreviousSmoothingMode(previous);
        scene.Canvas.ReturnPreviousSmoothingMode(null);

        scene.Canvas.PushAntiAlias(false);
        scene.Canvas.DrawRectangle(scene.Solid(255, 0, 0), 0.5, 0.5, 10, 10);
        scene.Canvas.PopAntiAlias();
        scene.Canvas.PopAntiAlias();
        scene.Canvas.PushAntiAlias(true);
        scene.Canvas.PopAntiAlias();

        Assert.True(scene.OpaquePixels() > 50);
    }

    [Fact]
    public void Tagging_calls_do_nothing_and_a_tile_is_offscreen()
    {
        using var scene = Scene.Create();
        scene.Canvas.BeginMarkedContent("/P", 1);
        scene.Canvas.EndMarkedContent();
        scene.Canvas.BeginArtifact();
        scene.Canvas.BeginVariableText();
        scene.Canvas.EndVariableText();
        Assert.False(scene.Canvas.IsOffscreenTile);
        Assert.Same(scene.Context, scene.Canvas.Context);

        var (tile, image) = scene.Canvas.CreateTile(5, 5)!.Value;
        Assert.True(tile.IsOffscreenTile);
        tile.Dispose();
        image.Dispose();
        Assert.Null(scene.Canvas.CreateTile(0, 5));
        Assert.Null(scene.Canvas.CreateTile(double.PositiveInfinity, 5));
    }

    [Fact]
    public void The_transform_scale_follows_the_pushed_transforms()
    {
        using var scene = Scene.Create();
        Assert.Equal((1.0, 1.0), scene.Canvas.TransformScale);
        scene.Canvas.PushTransform(Matrix3x2.CreateScale(2, 3));
        Assert.Equal(2.0, scene.Canvas.TransformScale.X, 3);
        Assert.Equal(3.0, scene.Canvas.TransformScale.Y, 3);
        scene.Canvas.PushTransform(Matrix3x2.CreateScale(0));
        Assert.Equal((1.0, 1.0), scene.Canvas.TransformScale);
        scene.Canvas.PopTransform();
        scene.Canvas.PopTransform();
    }

    [Fact]
    public void A_clipped_path_keeps_only_what_is_inside_the_rectangle()
    {
        using var scene = Scene.Create();
        using var path = Curve(scene.Canvas);
        using var clipped = path.ClipToRect(new Rect(0, 0, 10, 10));

        Assert.NotEmpty(clipped.GetCurveContours());
        Assert.Equal(path.FillMode, clipped.FillMode);
        using var outside = path.ClipToRect(new Rect(100, 100, 5, 5));
        Assert.Empty(outside.GetCurveContours());
    }

    // ---- images ---------------------------------------------------------------------------------------------

    private sealed class PixelImage(int size) : PeachDrawing.Core.Image
    {
        public override double Width => size;

        public override double Height => size;

        public override bool Interpolate { get; set; } = true;

        public override void Dispose()
        {
        }

        public override PixelBuffer? GetPixels()
        {
            var data = new byte[size * size * 4];
            for (var i = 0; i < data.Length; i += 4)
            {
                data[i] = 255;
                data[i + 3] = 255;
            }

            return new PixelBuffer(size, size, data);
        }
    }

    [Fact]
    public void An_image_from_another_backend_is_converted_once_and_released_with_the_canvas()
    {
        using var scene = Scene.Create();
        var foreign = new PixelImage(4);
        scene.Canvas.DrawImage(foreign, new Rect(0, 0, 10, 10));
        scene.Canvas.DrawImage(foreign, new Rect(10, 0, 10, 10), ImageSampling.Bicubic);
        scene.Canvas.DrawImage(foreign, new Rect(20, 0, 10, 10), new Rect(0, 0, 2, 2), ImageSampling.Pixelated);
        scene.Canvas.DrawImage(foreign, new Rect(0, 0, 0, 0));
        scene.Canvas.DrawImage(foreign, new Rect(0, 10, 10, 10), new Rect(0, 0, 0, 0));
        scene.Canvas.DrawImageWithOpacity(foreign, new Rect(0, 20, 10, 10), 0.5, PaintBlendMode.Plus);
        foreign.Interpolate = false;
        scene.Canvas.DrawImage(foreign, new Rect(10, 20, 10, 10));

        Assert.True(scene.IsRed(5, 5));
        Assert.True(scene.IsRed(15, 5));
        Assert.True(scene.IsRed(25, 5));
        Assert.True(scene.Px(5, 25).A > 0);
        Assert.True(scene.IsRed(15, 25));
        scene.Canvas.Dispose();
        Assert.Throws<NotSupportedException>(() => Scene.Create().Canvas.DrawImage(new Pixelless(), new Rect(0, 0, 4, 4)));
    }

    private sealed class Pixelless : PeachDrawing.Core.Image
    {
        public override double Width => 4;

        public override double Height => 4;

        public override bool Interpolate { get; set; }

        public override void Dispose()
        {
        }
    }

    [Theory]
    [InlineData(PaintBlendMode.Normal)]
    [InlineData(PaintBlendMode.Multiply)]
    [InlineData(PaintBlendMode.Screen)]
    [InlineData(PaintBlendMode.Overlay)]
    [InlineData(PaintBlendMode.Darken)]
    [InlineData(PaintBlendMode.Lighten)]
    [InlineData(PaintBlendMode.ColorDodge)]
    [InlineData(PaintBlendMode.ColorBurn)]
    [InlineData(PaintBlendMode.HardLight)]
    [InlineData(PaintBlendMode.SoftLight)]
    [InlineData(PaintBlendMode.Difference)]
    [InlineData(PaintBlendMode.Exclusion)]
    [InlineData(PaintBlendMode.Hue)]
    [InlineData(PaintBlendMode.Saturation)]
    [InlineData(PaintBlendMode.Color)]
    [InlineData(PaintBlendMode.Luminosity)]
    [InlineData(PaintBlendMode.Plus)]
    public void Two_tiles_blend_in_every_mode_and_a_blend_push_never_fails(PaintBlendMode mode)
    {
        using var scene = Scene.Create(20, 20);
        var (a, bottom) = scene.Canvas.CreateTile(20, 20)!.Value;
        a.DrawRectangle(scene.Solid(200, 100, 50), 0, 0, 20, 20);
        a.Dispose();
        var (b, top) = scene.Canvas.CreateTile(20, 20)!.Value;
        b.DrawRectangle(scene.Solid(50, 150, 200), 0, 0, 20, 20);
        b.Dispose();

        scene.Canvas.DrawImageBlendedOver(top, bottom, new Rect(0, 0, 20, 20), mode);
        scene.Canvas.DrawImageBlendedOver(top, bottom, new Rect(0, 0, 0, 0), mode);
        top.Dispose();
        bottom.Dispose();
        scene.Canvas.PushBlendMode(mode);
        scene.Canvas.DrawRectangle(scene.Solid(10, 10, 10), 0, 0, 5, 5);
        scene.Canvas.PopBlendMode();

        Assert.True(scene.Px(10, 10).A > 200);
    }

    [Fact]
    public void An_inverted_alpha_mask_shows_the_image_where_the_mask_is_clear()
    {
        using var scene = Scene.Create();
        var (content, contentImage) = scene.Canvas.CreateTile(40, 40)!.Value;
        content.DrawRectangle(scene.Solid(255, 0, 0), 0, 0, 40, 40);
        content.Dispose();
        var (mask, maskImage) = scene.Canvas.CreateTile(40, 40)!.Value;
        mask.DrawRectangle(scene.Solid(0, 0, 0), 0, 0, 20, 40);
        mask.Dispose();

        scene.Canvas.DrawImageAlphaMasked(contentImage, maskImage, new Rect(0, 0, 40, 40), invert: true);
        scene.Canvas.DrawImageAlphaMasked(contentImage, maskImage, new Rect(0, 0, 0, 0));
        scene.Canvas.DrawImageMasked(contentImage, maskImage, new Rect(0, 0, 0, 0));
        contentImage.Dispose();
        maskImage.Dispose();

        Assert.True(scene.IsClear(10, 20), scene.Px(10, 20).ToString());
        Assert.True(scene.IsRed(30, 20), scene.Px(30, 20).ToString());
    }

    [Fact]
    public void A_layer_with_bounds_a_blend_mode_and_a_colour_matrix_composites()
    {
        using var scene = Scene.Create();
        using (var layer = scene.Canvas.BeginLayer(new LayerOptions(Opacity: 1, BlendMode: PaintBlendMode.Plus, Bounds: new Rect(5, 5, 20, 20)))!)
            layer.Canvas.DrawRectangle(scene.Solid(255, 0, 0), 0, 0, 40, 40);

        Assert.True(scene.IsRed(10, 10));
        Assert.True(scene.IsClear(30, 30));
    }

    [Fact]
    public void A_layer_can_be_begun_inside_a_tile()
    {
        using var scene = Scene.Create();
        var (tile, image) = scene.Canvas.CreateTile(20, 20)!.Value;
        using (var layer = tile.BeginLayer(new LayerOptions(Opacity: 0.5))!)
            layer.Canvas.DrawRectangle(scene.Solid(255, 0, 0), 0, 0, 20, 20);

        tile.Dispose();
        scene.Canvas.DrawImage(image, new Rect(0, 0, 20, 20));
        image.Dispose();
        Assert.InRange(scene.Px(10, 10).A, 100, 150);
    }

    // ---- glyphs ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task Bitmap_colour_and_svg_glyph_fonts_each_draw_through_their_own_path()
    {
        using var scene = Scene.Create(120, 60);
        foreach (var (path, name) in new[] { (BundledFonts.RealCbdt, "Cbdt"), (BundledFonts.RealSbix, "Sbix"), (BundledFonts.ColorV1, "ColorV1"), (BundledFonts.SvgTest, "SvgTest") })
        {
            await using var stream = File.OpenRead(path);
            await scene.Context.AddFont(stream, name);
            var font = scene.Context.GetFont(name, 30, PaintFontStyle.Regular)!;
            var palette = new FontPalette(0, new[] { new KeyValuePair<int, PaintColor>(0, PaintColor.FromArgb(255, 0, 255, 0)) });
            scene.Canvas.DrawString("AB", font, PaintColor.Black, new PaintPoint(2, 2), new Core.Size(60, 30), 0, palette);
            scene.Canvas.DrawString("AB", font, PaintColor.Black, new PaintPoint(2, 2), new Core.Size(60, 30));
        }

        scene.Canvas.Dispose();
    }

    [Fact]
    public async Task Synthetic_bold_and_italic_text_and_the_remaining_text_entry_points()
    {
        using var scene = await Scene.WithFontAsync(160, 80);
        var font = scene.Context.GetFont("Source Sans 3", 24, PaintFontStyle.Bold | PaintFontStyle.Italic, weight: 900)!;

        scene.Canvas.DrawString("Hello", font, PaintColor.Black, new PaintPoint(2, 2), new Core.Size(80, 30), 1, null, ShapeSettings.Default, "olleH");
        scene.Canvas.DrawStringInRect("Hello", font, PaintColor.Black, new Rect(2, 40, 100, 30), BoxAlignment.Center, BoxAlignment.Center);
        scene.Canvas.DrawStringInRect("", font, PaintColor.Black, new Rect(2, 40, 100, 30), BoxAlignment.End, BoxAlignment.End);
        scene.Canvas.DrawGlyphs(new[] { new GlyphPlacement(font.GetGlyphIndex(new System.Text.Rune('H')), 120, 60) }, font, PaintColor.Black);
        scene.Canvas.DrawGlyphs(new[] { new GlyphPlacement(font.GetGlyphIndex(new System.Text.Rune('H')), 120, 60) }, font, PaintColor.Transparent);

        scene.Canvas.MeasureString("Hello world", font, 60, out var fit, out var width);
        Assert.InRange(fit, 1, 10);
        Assert.True(width > 0 && width <= 60);
        scene.Canvas.MeasureString("\U0001F600\U0001F600", font, 1000, out var emojiFit, out _);
        Assert.Equal(4, emojiFit);
        scene.Canvas.MeasureString("\U0001F600", font, 0, out var none, out _);
        Assert.Equal(0, none);

        Assert.Null(scene.Canvas.GetInkCrossings("Hi", font, new PaintPoint(0, 0), 10, 5));
        Assert.NotNull(scene.Canvas.GetInkCrossings("Hi", font, new PaintPoint(0, 0), 14, 18, 1));
        Assert.True(scene.OpaquePixels() > 100);
    }

    // ---- links, images and the composer -----------------------------------------------------------------------

    [Fact]
    public void Link_hit_testing_in_device_pixels_follows_the_sessions_scale()
    {
        using var scene = Scene.Create(40, 40, pixelsPerPoint: 2);
        scene.Canvas.AddLink(new Rect(10, 10, 10, 10), "https://example.com/");
        Assert.NotNull(scene.Canvas.Links.HitTestDips(15, 15));
        Assert.Null(scene.Canvas.Links.HitTestDips(25, 25));
        Assert.Empty(scene.Canvas.Links.Destinations);
        Assert.Throws<ArgumentNullException>(() => scene.Canvas.AddLink(new Rect(0, 0, 1, 1), null!));
        Assert.Throws<ArgumentException>(() => scene.Canvas.AddDestinationLink(new Rect(0, 0, 1, 1), ""));
        Assert.Throws<ArgumentException>(() => scene.Canvas.AddDestination("", 1));
        Assert.Throws<ArgumentNullException>(() => scene.Canvas.Links.ActivateAsync(null!).GetAwaiter().GetResult());
    }

    [Fact]
    public async Task A_link_with_no_address_cannot_be_activated_and_an_activation_through_the_default_launcher_is_refused_for_a_relative_one()
    {
        using var scene = Scene.Create();
        scene.Canvas.AddDestinationLink(new Rect(0, 0, 10, 10), "x");
        Assert.False(await scene.Canvas.Links.TryActivateAsync(5, 5));

        scene.Canvas.AddLink(new Rect(20, 0, 10, 10), "relative.html");
        Assert.False(await scene.Canvas.Links.TryActivateAsync(25, 5));
    }

    [Theory]
    [InlineData(Win2dImageFormat.Png)]
    [InlineData(Win2dImageFormat.Jpeg)]
    [InlineData(Win2dImageFormat.Bmp)]
    [InlineData(Win2dImageFormat.Tiff)]
    [InlineData(Win2dImageFormat.Gif)]
    public async Task The_page_saves_in_every_format(Win2dImageFormat format)
    {
        using var scene = Scene.Create();
        scene.Canvas.DrawRectangle(scene.Solid(255, 0, 0), 0, 0, 10, 10);
        using var stream = new MemoryStream();
        await scene.Composer.SaveAsync(stream, format);

        Assert.True(stream.Length > 20);
        Assert.Equal(40, scene.Composer.PixelWidth);
        Assert.Equal(40, scene.Composer.PixelHeight);
        Assert.Equal(40, scene.Composer.WidthPoints);
        Assert.Equal(40, scene.Composer.HeightPoints);
        Assert.Same(scene.Context, scene.Composer.RenderContext);
        await Assert.ThrowsAsync<ArgumentNullException>(() => scene.Composer.SaveAsync(null!));
    }

    [Fact]
    public void A_composer_disposed_refuses_to_finish_or_read()
    {
        var context = Win2dRenderContext.CreateSoftware();
        var composer = new Win2dComposer(context, 10, 10);
        composer.Dispose();

        Assert.Throws<ObjectDisposedException>(composer.Finish);
        context.Dispose();
    }

    [Fact]
    public void A_stream_that_is_not_a_memory_stream_is_decoded_and_garbage_is_not()
    {
        using var context = Win2dRenderContext.CreateSoftware();
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".png");
        try
        {
            var raster = new RasterRenderContext();
            using (var canvas = raster.CreateCanvas(4, 4))
            {
                canvas.DrawRectangle(raster.GetSolidBrush(PaintColor.FromArgb(128, 255, 0, 0)), 0, 0, 4, 4);
                using var file = File.Create(path);
                canvas.Save(file, "png", new PeachImage.Formats.Png.PngEncoderOptions());
            }

            using var read = File.OpenRead(path);
            using var image = context.ImageFromStream(read);
            Assert.Equal(4, image.Width);
            Assert.NotNull(image.GetPixels());
            Assert.Equal(1, image.ExifOrientation);

            using var fromPixels = Win2dImage.FromPixels(image.GetPixels()!.Value);
            Assert.Equal(4, fromPixels.Height);
            Assert.Null(Win2dImage.TryDecode(new MemoryStream([9, 9, 9])));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void The_link_map_hit_tests_a_quad_with_a_horizontal_edge_and_an_empty_map()
    {
        var link = new Win2dLink(new Rect(0, 0, 10, 10), new List<PaintPoint> { new(0, 0), new(10, 0), new(0, 10), new(10, 10) }, "https://example.com/", null);
        Assert.True(link.Contains(5, 5));
        Assert.False(link.Contains(15, 5));
        Assert.False(new Win2dLink(new Rect(0, 0, 10, 10), new List<PaintPoint> { new(0, 0) }, "x", null).Contains(20, 20));
    }

    [Fact]
    public void Layout_widths_scale_with_pixels_per_point_for_both_engines()
    {
        foreach (var engine in new[] { Win2dTextEngine.PeachDrawing, Win2dTextEngine.DirectWrite })
        {
            using var small = Scene.Create(engine: engine);
            using var big = Scene.Create(pixelsPerPoint: 2, engine: engine);
            var narrow = small.Canvas.MeasureString("Wide text", small.Context.GetFont("Segoe UI", 16, PaintFontStyle.Regular)!).Width;
            var wide = big.Canvas.MeasureString("Wide text", big.Context.GetFont("Segoe UI", 32, PaintFontStyle.Regular)!).Width;

            Assert.InRange(wide / narrow, 1.8, 2.2);
        }
    }

    [Fact]
    public void A_device_error_on_a_healthy_device_passes_through_and_the_lost_device_exception_wraps_its_cause()
    {
        using var scene = Scene.Create();
        var guarded = typeof(Win2dComposer).GetMethods(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            .Where(m => m.Name == "Guarded").ToArray();

        // The device is healthy, so Win2D does not call the error a loss and it is left to the caller.
        foreach (var method in guarded)
        {
            object action = method.IsGenericMethodDefinition
                ? (Func<int>)(() => throw new System.Runtime.InteropServices.COMException("removed", unchecked((int)0x887A0005)))
                : (Action)(() => throw new System.Runtime.InteropServices.COMException("removed", unchecked((int)0x887A0005)));
            var target = method.IsGenericMethodDefinition ? method.MakeGenericMethod(typeof(int)) : method;
            var failure = Assert.Throws<System.Reflection.TargetInvocationException>(() => target.Invoke(scene.Composer, [action]));
            Assert.IsType<System.Runtime.InteropServices.COMException>(failure.InnerException);
        }

        var cause = new InvalidOperationException("device removed");
        var lost = new Win2dDeviceLostException(cause);
        Assert.Same(cause, lost.InnerException);
        Assert.Contains("lost", lost.Message);
    }

    [Fact]
    public void A_conic_gradient_with_stops_that_do_not_start_at_zero_wraps_and_interpolates()
    {
        using var scene = Scene.Create(60, 60);
        var conic = scene.Context.GetConicGradientBrush(new PaintPoint(30, 30), 28,
            [PaintColor.FromArgb(255, 255, 0, 0), PaintColor.FromArgb(0, 0, 255, 0), PaintColor.FromArgb(255, 0, 0, 255)], [1, 3, 5]);
        scene.Canvas.DrawRectangle(conic, 0, 0, 60, 60);

        Assert.True(scene.OpaquePixels() > 500);
    }
}
