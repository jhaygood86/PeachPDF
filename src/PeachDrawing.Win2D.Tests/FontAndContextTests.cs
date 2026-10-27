using Microsoft.Graphics.Canvas;
using PeachDrawing.Core;
using PeachDrawing.Text;
using PeachDrawing.Text.Shaping;
using PeachDrawing.Win2D;
using PeachPDF.Tests.TestSupport;

namespace PeachDrawing.Win2D.Tests;

/// <summary>The fonts both engines hand out, and the render context that makes them.</summary>
public class FontAndContextTests
{
    private static async Task<Win2dRenderContext> ContextWithFontAsync(Win2dTextEngine engine = Win2dTextEngine.PeachDrawing)
    {
        var context = Win2dRenderContext.CreateSoftware(engine);
        await using var stream = File.OpenRead(BundledFonts.Ttf);
        await context.AddFont(stream, "Source Sans 3");
        return context;
    }

    [Fact]
    public async Task A_PeachDrawing_font_answers_every_metric_and_capability_question()
    {
        using var context = await ContextWithFontAsync();
        var font = context.GetFont("Source Sans 3", 24, PaintFontStyle.Bold | PaintFontStyle.Italic, weight: 700)!;
        using var canvas = new Win2dComposer(context, 40, 40);

        Assert.True(font.Size > 0);
        Assert.True(font.Height > 0);
        Assert.True(font.Ascent > 0);
        Assert.True(font.TextBaselineOffset > 0);
        Assert.True(font.NormalLineHeight > 0);
        Assert.True(font.UnderlineOffset > 0);
        Assert.True(font.UnderlineThickness > 0);
        _ = font.UnderlinePosition;
        Assert.True(font.LeftPadding > 0);
        Assert.NotNull(font.Typeface);
        _ = font.SyntheticStyle;
        _ = font.ObliqueSkewSinus;
        Assert.True(font.GetWhitespaceWidth(canvas.Canvas) > 0);
        Assert.True(font.GetWhitespaceWidth(canvas.Canvas) > 0);
        Assert.True(font.HasGlyph(new System.Text.Rune('A')));
        Assert.False(font.HasGlyph(new System.Text.Rune(0x1F600)) && font.HasGlyph(new System.Text.Rune(0x10FFFD)));
        _ = font.MatchesEmojiPresentation(new System.Text.Rune('A'), PeachDrawing.Text.Unicode.EmojiPresentation.NoPreference);
        _ = font.SupportsFontVariantCaps(CapsMode.SmallCaps);
        _ = font.SupportsFontVariantPosition(SubSuperMode.Super);
        _ = font.GetSubSuperscriptMetrics(true);
        _ = font.GetSubSuperscriptMetrics(false);
        Assert.False(string.IsNullOrEmpty(font.FaceKey));
        Assert.Equal(font.FaceKey, font.FaceKey);
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
        Assert.True(font.FontUnitsPerEm > 0);
        Assert.True(font.GetGlyphIndex(new System.Text.Rune('A')) > 0);
        Assert.True(font.GetGlyphAdvanceWidthDesignUnits(font.GetGlyphIndex(new System.Text.Rune('A'))) > 0);
        Assert.NotNull(font.XHeightEm);
        Assert.NotNull(font.CapHeightEm);
        _ = font.GetBaselineHeightEm("ideo", false);
    }

    [Fact]
    public async Task Colour_font_palettes_are_read_through_the_font()
    {
        using var context = Win2dRenderContext.CreateSoftware();
        await using (var stream = File.OpenRead(BundledFonts.ColorV0))
            await context.AddFont(stream, "Color Test");

        var font = context.GetFont("Color Test", 20, PaintFontStyle.Regular)!;
        Assert.True(font.PaletteCount > 0);
        Assert.True(font.PaletteEntryCount > 0);
        Assert.True(font.TryGetPaletteColor(0, 0, out var colour));
        Assert.NotEqual(default, colour);
        _ = font.FirstLightPalette();
        _ = font.FirstDarkPalette();
    }

    [Fact]
    public async Task Fonts_are_matched_by_codepoint_and_by_the_system_fallback()
    {
        using var context = await ContextWithFontAsync();

        Assert.NotNull(context.GetFontForCodepoint("Source Sans 3", 16, PaintFontStyle.Regular, new System.Text.Rune('A')));
        Assert.Null(context.GetFontForCodepoint("No Such Family", 16, PaintFontStyle.Regular, new System.Text.Rune('A')));
        Assert.NotNull(context.GetSystemFallbackFontForCodepoint(16, PaintFontStyle.Regular, new System.Text.Rune('A')));
        Assert.Null(context.GetSystemFallbackFontForCodepoint(16, PaintFontStyle.Regular, new System.Text.Rune(0x10FFFD)));
        _ = context.FamilyHasExplicitUnicodeRanges("Source Sans 3");
        Assert.True(context.IsFontExists("Source Sans 3"));
    }

    [Fact]
    public async Task Fonts_can_be_added_from_bytes_streams_and_local_faces_and_refuse_unsupported_formats()
    {
        using var context = Win2dRenderContext.CreateSoftware();
        var bytes = await File.ReadAllBytesAsync(BundledFonts.Ttf);

        Assert.True(await context.AddFontFromBytes("From Bytes", bytes));
        Assert.True(await context.AddFontFromBytes("Declared", bytes, new FontFaceDescriptors()));
        await using (var stream = new MemoryStream(bytes))
            await context.AddFont(stream, "From Stream");

        // A relative address has nowhere to be fetched from, and this context has no resource loader.
        Assert.False(await context.AddFontFamilyFromUrl("Nowhere", "relative.ttf", null));
        Assert.False(await context.AddFontFamilyFromUrl("Nowhere", "https://example.invalid/x.ttf", null));

        Assert.NotNull(context.GetFont("From Bytes", 16, PaintFontStyle.Regular));
        Assert.False(await context.AddLocalFontFamily("Local", "No Such Installed Face"));
        _ = await context.AddLocalFontFamily("Local", "Source Sans 3");
        _ = await context.AddLocalFontFamily("Local", "Source Sans 3 Regular");
    }

    [Fact]
    public void Colours_are_resolved_by_name_and_an_unknown_name_is_empty()
    {
        using var context = Win2dRenderContext.CreateSoftware();
        Assert.Equal(PaintColor.FromArgb(255, 255, 0, 0), context.GetColor("red"));
        Assert.True(context.GetColor("notacolour").IsEmpty);
    }

    [Fact]
    public void The_media_type_prefers_screen()
    {
        using var context = Win2dRenderContext.CreateSoftware();
        Assert.Equal("screen", context.GetCssMediaType(["print", "screen"]));
        Assert.Equal("all", context.GetCssMediaType(["print"]));
    }

    [Fact]
    public void Garbage_bytes_are_not_an_image()
    {
        using var context = Win2dRenderContext.CreateSoftware();
        Assert.Throws<InvalidDataException>(() => context.ImageFromStream(new MemoryStream([1, 2, 3, 4])));
    }

    [Fact]
    public void A_device_the_caller_gave_is_used_and_a_lost_one_is_replaced_by_one_the_context_owns()
    {
        using var device = new CanvasDevice(true);
        var context = new Win2dRenderContext(device);
        var lost = 0;
        context.DeviceLost += (_, _) => lost++;
        Assert.Same(device, context.Device);

        Lose(context, device);
        Assert.Equal(1, lost);

        // The borrowed device is not ours to dispose; the replacement is.
        var replacement = context.Device;
        Assert.NotSame(device, replacement);
        context.Dispose();
        Assert.ThrowsAny<Exception>(() => new CanvasRenderTarget(replacement, 4, 4, 96));
        using var stillGood = new CanvasRenderTarget(device, 4, 4, 96);
    }

    [Fact]
    public void A_lost_device_the_context_made_is_disposed_and_replaced()
    {
        var context = Win2dRenderContext.CreateSoftware();
        var first = context.Device;
        Lose(context, first);

        Assert.NotSame(first, context.Device);
        context.Dispose();
    }

    // A healthy device cannot be made to raise DeviceLost (RaiseDeviceLost rejects it), so the context's own handler is called directly.
    private static void Lose(Win2dRenderContext context, CanvasDevice device) =>
        typeof(Win2dRenderContext).GetMethod("OnDeviceLost", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .Invoke(context, [device, new object()]);

    [Fact]
    public void A_context_makes_its_own_device_lazily_and_refuses_one_after_it_is_disposed()
    {
        var context = new Win2dRenderContext();
        Assert.Equal(Win2dTextEngine.PeachDrawing, context.TextEngine);
        Assert.NotNull(context.Device);
        context.Dispose();
        context.Dispose();
        Assert.Throws<ObjectDisposedException>(() => context.Device);
    }

    [Fact]
    public void The_layout_unit_scale_is_the_pixels_per_point()
    {
        using var context = Win2dRenderContext.CreateSoftware();
        context.PixelsPerPoint = 2;
        Assert.Equal(2, context.LayoutUnitsPerPoint);
        var font = context.GetFont("Segoe UI", 20, PaintFontStyle.Regular)!;
        Assert.Equal(10, font.Size, 3);
    }

    // ---- DirectWrite --------------------------------------------------------------------------------------

    [Theory]
    [InlineData(100, 40)]
    [InlineData(300, 60)]
    [InlineData(400, 80)]
    [InlineData(500, 90)]
    [InlineData(700, 100)]
    [InlineData(900, 120)]
    [InlineData(400, 140)]
    [InlineData(400, 160)]
    [InlineData(400, 200)]
    public void DirectWrite_fonts_cover_the_weight_and_stretch_range(int weight, int stretch)
    {
        using var scene = Scene.Create(120, 40, engine: Win2dTextEngine.DirectWrite);
        var font = scene.Context.GetFont("Segoe UI", 18, PaintFontStyle.Italic, weight, stretch)!;

        Assert.True(scene.Canvas.MeasureString("Weight", font).Width > 10);
        Assert.Equal(font.FaceKey, scene.Context.GetFont("Segoe UI", 18, PaintFontStyle.Italic, weight, stretch)!.FaceKey);
    }

    [Fact]
    public async Task A_DirectWrite_font_answers_every_metric_and_capability_question()
    {
        using var context = await ContextWithFontAsync(Win2dTextEngine.DirectWrite);
        using var composer = new Win2dComposer(context, 40, 40);
        var font = context.GetFont("Source Sans 3", 24, PaintFontStyle.Regular)!;

        Assert.True(font.Size > 0);
        Assert.True(font.Height > 0);
        Assert.True(font.Ascent > 0);
        Assert.True(font.TextBaselineOffset > 0);
        Assert.True(font.NormalLineHeight > 0);
        Assert.True(font.UnderlineOffset > 0);
        Assert.True(font.LeftPadding > 0);
        Assert.True(font.GetWhitespaceWidth(composer.Canvas) > 0);
        Assert.True(font.GetWhitespaceWidth(composer.Canvas) > 0);
        Assert.True(font.HasGlyph(new System.Text.Rune('A')));
        _ = font.SupportsFontVariantCaps(CapsMode.SmallCaps);
        _ = font.SupportsFontVariantPosition(SubSuperMode.Super);
        _ = font.GetSubSuperscriptMetrics(true);
        _ = font.GetSubSuperscriptMetrics(false);
        Assert.StartsWith("dw/", font.FaceKey);

        // A family that is not installed is assumed to cover everything, so the caller never loops on it.
        var unmatched = new DirectWriteFont(context, "Not Installed Anywhere", 12, PaintFontStyle.Regular, 400, 100);
        Assert.True(unmatched.HasGlyph(new System.Text.Rune('A')));
        Assert.False(unmatched.SupportsFontVariantCaps(CapsMode.SmallCaps));
        Assert.Null(unmatched.GetSubSuperscriptMetrics(true));
    }

    [Fact]
    public void DirectWrite_fonts_for_a_codepoint_and_the_fallback_chain()
    {
        using var context = Win2dRenderContext.CreateSoftware(Win2dTextEngine.DirectWrite);

        Assert.NotNull(context.GetFontForCodepoint("Segoe UI", 16, PaintFontStyle.Regular, new System.Text.Rune('A')));
        Assert.Null(context.GetFontForCodepoint("No Such Family", 16, PaintFontStyle.Regular, new System.Text.Rune('A')));
        Assert.NotNull(context.GetSystemFallbackFontForCodepoint(16, PaintFontStyle.Regular, new System.Text.Rune('A')));
        Assert.Null(context.GetSystemFallbackFontForCodepoint(16, PaintFontStyle.Regular, new System.Text.Rune(0x10FFFD)));
    }

    [Fact]
    public void DirectWrite_features_kerning_and_ligatures_are_requested_without_failing()
    {
        using var scene = Scene.Create(200, 40, engine: Win2dTextEngine.DirectWrite);
        var font = scene.Context.GetFont("Segoe UI", 20, PaintFontStyle.Regular)!;

        var features = new ShapeSettings(
            Ligatures: LigatureSet.None,
            Kerning: false,
            ExplicitFeatures: [new FeatureSetting("smcp", 1), new FeatureSetting("liga", 0), new FeatureSetting("toolong", 1)]);

        Assert.True(scene.Canvas.MeasureString("office fi", font, features).Width > 0);
        Assert.True(scene.Canvas.CountShapedGlyphs("office", font, features) > 0);
        Assert.Equal(0, scene.Canvas.CountShapedGlyphs("", font));
        Assert.Equal(0, scene.Canvas.MeasureString("", font).Width);
        scene.Canvas.DrawString("office", font, PaintColor.Black, new PaintPoint(2, 2), new Core.Size(80, 20), 1, null, features);

        // A transparent run draws nothing; an empty or invisible one is skipped.
        scene.Canvas.DrawString("x", font, PaintColor.Transparent, new PaintPoint(2, 2), new Core.Size(8, 20));
        scene.Canvas.DrawString("", font, PaintColor.Black, new PaintPoint(2, 2), new Core.Size(0, 20));
        Assert.True(scene.OpaquePixels() > 20);
    }

    [Fact]
    public async Task DirectWrite_draws_glyphs_by_index_when_the_family_is_also_a_PeachDrawing_font()
    {
        using var scene = Scene.Create(80, 60, engine: Win2dTextEngine.DirectWrite);
        await using (var stream = File.OpenRead(BundledFonts.Ttf))
            await scene.Context.AddFont(stream, "Source Sans 3");

        var font = scene.Context.GetFont("Source Sans 3", 30, PaintFontStyle.Regular)!;
        scene.Canvas.DrawGlyphs(new[] { new GlyphPlacement(font.GetGlyphIndex(new System.Text.Rune('H')), 5, 40) }, font, PaintColor.Black);

        // With no PeachDrawing typeface behind it, a glyph run is skipped rather than failing.
        var unmatched = new DirectWriteFont(scene.Context, "No Such Family At All", 30, PaintFontStyle.Regular, 400, 100);
        scene.Canvas.DrawGlyphs(new[] { new GlyphPlacement(5, 5, 40) }, unmatched, PaintColor.Black);
        Assert.True(scene.OpaquePixels() > 20);
    }
}
