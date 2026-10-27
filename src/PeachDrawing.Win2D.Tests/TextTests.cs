using PeachDrawing.Core;
using PeachDrawing.Win2D;
using PeachPDF.Tests.TestSupport;

namespace PeachDrawing.Win2D.Tests;

public class TextTests
{
    private static Font SourceSans(Scene scene, double size = 20) =>
        scene.Context.GetFont("Source Sans 3", size, PaintFontStyle.Regular)!;

    [Fact]
    public async Task Text_drawn_with_the_shared_engine_paints_and_measures_the_same_run()
    {
        using var scene = await Scene.WithFontAsync();
        var font = SourceSans(scene);

        var size = scene.Canvas.MeasureString("Hello", font);
        scene.Canvas.DrawString("Hello", font, PaintColor.FromArgb(255, 255, 0, 0), new PaintPoint(5, 5), size);

        Assert.True(size.Width > 30 && size.Width < 80, size.Width.ToString());
        Assert.True(scene.OpaquePixels() > 100);

        // Nothing is inked to the right of the measured run.
        var farRight = Enumerable.Range((int)(5 + size.Width) + 3, 20).Sum(x => Enumerable.Range(0, 60).Count(y => scene.Px(x, y).A > 0));
        Assert.Equal(0, farRight);
    }

    [Fact]
    public async Task Invisible_text_draws_nothing()
    {
        using var scene = await Scene.WithFontAsync();
        scene.Canvas.InvisibleText = true;
        var font = SourceSans(scene);
        scene.Canvas.DrawString("Hello", font, PaintColor.FromArgb(255, 255, 0, 0), new PaintPoint(5, 5), new Core.Size(50, 20));

        Assert.Equal(0, scene.OpaquePixels());
    }

    [Fact]
    public async Task A_run_has_a_vector_outline_and_ink_crossings()
    {
        using var scene = await Scene.WithFontAsync();
        var font = SourceSans(scene);

        using var outline = scene.Canvas.GetTextOutline("Hi", font, new PaintPoint(0, 30));
        Assert.NotNull(outline);
        Assert.NotEmpty(outline!.GetCurveContours());

        var crossings = scene.Canvas.GetInkCrossings("Hi", font, new PaintPoint(0, 0), 14, 16);
        Assert.NotNull(crossings);
    }

    [Fact]
    public async Task Letter_spacing_widens_the_run_that_is_drawn()
    {
        using var scene = await Scene.WithFontAsync();
        var font = SourceSans(scene);
        scene.Canvas.DrawString("III", font, PaintColor.FromArgb(255, 255, 0, 0), new PaintPoint(0, 0), new Core.Size(50, 20), letterSpacing: 10);

        var rightmost = Enumerable.Range(0, 200).Last(x => Enumerable.Range(0, 60).Any(y => scene.Px(x, y).A > 0));
        Assert.True(rightmost > 25, rightmost.ToString());
    }

    [Fact]
    public async Task Glyphs_can_be_drawn_by_index()
    {
        using var scene = await Scene.WithFontAsync();
        var font = SourceSans(scene, 30);
        var glyph = font.GetGlyphIndex(new System.Text.Rune('H'));

        scene.Canvas.DrawGlyphs(new[] { new GlyphPlacement(glyph, 5, 40) }, font, PaintColor.FromArgb(255, 255, 0, 0));
        Assert.True(scene.OpaquePixels() > 50);
    }

    [Fact]
    public async Task A_colour_font_glyph_is_drawn_in_its_own_colours()
    {
        using var scene = Scene.Create(80, 40);
        await using (var stream = File.OpenRead(BundledFonts.ColorV0))
            await scene.Context.AddFont(stream, "Color Test");

        var font = scene.Context.GetFont("Color Test", 30, PaintFontStyle.Regular)!;
        scene.Canvas.DrawString("A", font, PaintColor.Black, new PaintPoint(5, 5), new Core.Size(30, 30));

        Assert.True(scene.OpaquePixels() > 0);
    }

    [Fact]
    public void A_font_is_found_for_a_codepoint_and_for_the_fallback_chain()
    {
        using var scene = Scene.Create();
        Assert.NotNull(scene.Context.GetFont("Segoe UI", 16, PaintFontStyle.Regular));
        Assert.NotNull(scene.Context.GetSystemFallbackFontForCodepoint(16, PaintFontStyle.Regular, new System.Text.Rune('A')));
    }

    // ---- DirectWrite --------------------------------------------------------------------------------------

    [Fact]
    public void DirectWrite_measures_and_draws_an_installed_font()
    {
        using var scene = Scene.Create(120, 40, engine: Win2dTextEngine.DirectWrite);
        var font = scene.Context.GetFont("Segoe UI", 20, PaintFontStyle.Regular)!;
        Assert.IsNotType<Win2dFont>(font);

        var size = scene.Canvas.MeasureString("Hello world", font);
        var wide = scene.Canvas.MeasureString("Hello world, hello world", font);
        scene.Canvas.DrawString("Hello world", font, PaintColor.FromArgb(255, 255, 0, 0), new PaintPoint(5, 5), size);
        var clusters = scene.Canvas.CountShapedGlyphs("fi", font);

        Assert.True(size.Width > 50 && size.Width < 140, size.Width.ToString());
        Assert.True(wide.Width > size.Width * 1.8);
        Assert.True(font.Height > 15 && font.Height < 40);
        Assert.True(font.Ascent > 10 && font.Ascent < font.Height);
        Assert.InRange(clusters, 1, 2);
        Assert.True(scene.OpaquePixels() > 100);
    }

    [Fact]
    public void DirectWrite_letter_spacing_and_features_change_the_drawn_run()
    {
        using var scene = Scene.Create(200, 40, engine: Win2dTextEngine.DirectWrite);
        var font = scene.Context.GetFont("Segoe UI", 20, PaintFontStyle.Regular)!;

        var plain = scene.Canvas.MeasureString("office", font, new PeachDrawing.Text.Shaping.ShapeSettings());
        var noKerning = scene.Canvas.MeasureString("office", font, new PeachDrawing.Text.Shaping.ShapeSettings(Kerning: false));
        scene.Canvas.DrawString("III", font, PaintColor.FromArgb(255, 255, 0, 0), new PaintPoint(0, 0), new Core.Size(50, 20), letterSpacing: 15);

        var rightmost = Enumerable.Range(0, 200).Last(x => Enumerable.Range(0, 40).Any(y => scene.Px(x, y).A > 0));
        Assert.True(rightmost > 35, rightmost.ToString());
        Assert.True(plain.Width > 0 && noKerning.Width > 0);
    }

    [Fact]
    public void DirectWrite_has_no_outlines_so_the_caller_falls_back_to_drawing_the_string()
    {
        using var scene = Scene.Create(engine: Win2dTextEngine.DirectWrite);
        var font = scene.Context.GetFont("Segoe UI", 20, PaintFontStyle.Regular)!;
        Assert.Null(scene.Canvas.GetTextOutline("Hi", font, new PaintPoint(0, 20)));
    }

    [Fact]
    public async Task DirectWrite_draws_a_font_the_document_added_and_cleans_up_its_file()
    {
        var scene = Scene.Create(150, 50, engine: Win2dTextEngine.DirectWrite);
        await using (var stream = File.OpenRead(BundledFonts.Ttf))
            await scene.Context.AddFont(stream, "Added Face");

        var added = scene.Context.GetFont("Added Face", 24, PaintFontStyle.Regular)!;
        var fallback = scene.Context.GetFont("Segoe UI", 24, PaintFontStyle.Regular)!;
        var addedWidth = scene.Canvas.MeasureString("Hamburgefonstiv", added).Width;
        var fallbackWidth = scene.Canvas.MeasureString("Hamburgefonstiv", fallback).Width;
        scene.Canvas.DrawString("Hamburg", added, PaintColor.Black, new PaintPoint(2, 2), new Core.Size(100, 30));

        // The added font is really used: its widths are not the system font's.
        Assert.NotEqual(Math.Round(fallbackWidth), Math.Round(addedWidth));
        Assert.True(scene.OpaquePixels() > 100);

        var folder = Path.Combine(Path.GetTempPath(), "PeachDrawing.Win2D");
        var before = Directory.Exists(folder) ? Directory.GetDirectories(folder).Length : 0;
        scene.Dispose();
        var after = Directory.Exists(folder) ? Directory.GetDirectories(folder).Length : 0;
        Assert.True(after < before, $"{before} -> {after}");
    }
}
