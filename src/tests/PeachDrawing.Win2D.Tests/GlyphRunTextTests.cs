using PeachDrawing.Core;
using PeachDrawing.Text.Shaping;
using PeachDrawing.Win2D;
using PeachPDF.Tests.TestSupport;
using System.Numerics;

namespace PeachDrawing.Win2D.Tests;

/// <summary>
/// Glyphs addressed by index (what a PDF's text is) drawn with <see cref="Win2dTextRendering"/>: as the same hinted, weight-corrected coverage a string
/// gets, with ClearType stripes where they apply, and as outlines where a mask cannot be made.
/// </summary>
public class GlyphRunTextTests
{
    private const string Sample = "Hamburgefonstiv";

    private static async Task<Scene> SceneAsync(Win2dTextRendering mode, double width = 200, double height = 60)
    {
        var scene = Scene.Create(width, height, options: new Win2dComposerOptions { Background = PaintColor.White });
        scene.Context.TextRendering = mode;
        await using var stream = File.OpenRead(BundledFonts.Ttf);
        await scene.Context.AddFont(stream, "Source Sans 3");
        return scene;
    }

    /// <summary>The run's glyphs with their origins, as a PDF page's text would place them: the pen advancing by each glyph's width.</summary>
    private static List<GlyphPlacement> Place(Scene scene, Font font, string text, double x, double baseline)
    {
        var typeface = font.Typeface!;
        var scale = font.Size / typeface.Metrics.UnitsPerEm * scene.Context.PixelsPerPoint;
        var placements = new List<GlyphPlacement>();
        var pen = x;
        foreach (var glyph in Shaper.Shape(typeface, text, ShapeSettings.Default).Glyphs)
        {
            placements.Add(new GlyphPlacement(glyph.GlyphIndex, pen, baseline, ""));
            pen += typeface.GetAdvance((ushort)glyph.GlyphIndex) * scale;
        }

        return placements;
    }

    private static void DrawRun(Scene scene, PaintColor color, double size = 16, double x = 5, double baseline = 24, Action<Win2dCanvas>? before = null)
    {
        var font = scene.Context.GetFont("Source Sans 3", size, PaintFontStyle.Regular)!;
        before?.Invoke(scene.Canvas);
        scene.Canvas.DrawGlyphs(Place(scene, font, Sample, x, baseline), font, color);
    }

    private static (int Left, int Top, int Right, int Bottom)? InkBounds(Scene scene)
    {
        var (left, top, right, bottom) = (int.MaxValue, int.MaxValue, -1, -1);
        for (var y = 0; y < scene.Composer.PixelHeight; y++)
        for (var x = 0; x < scene.Composer.PixelWidth; x++)
        {
            var p = scene.Px(x, y);
            if (p.R > 250 && p.G > 250 && p.B > 250)
                continue;

            left = Math.Min(left, x);
            right = Math.Max(right, x);
            top = Math.Min(top, y);
            bottom = Math.Max(bottom, y);
        }

        return right < 0 ? null : (left, top, right, bottom);
    }

    private static long Darkness(Scene scene)
    {
        long total = 0;
        for (var y = 0; y < scene.Composer.PixelHeight; y++)
        for (var x = 0; x < scene.Composer.PixelWidth; x++)
        {
            var p = scene.Px(x, y);
            total += 255 * 3 - (p.R + p.G + p.B);
        }

        return total;
    }

    [Fact]
    public async Task GrayscaleGlyphs_AreInkedWhereOutlineGlyphsAre_AndNotTheSamePixels()
    {
        using var outline = await SceneAsync(Win2dTextRendering.Outline);
        using var gray = await SceneAsync(Win2dTextRendering.Grayscale);
        DrawRun(outline, PaintColor.Black);
        DrawRun(gray, PaintColor.Black);

        var a = InkBounds(outline)!.Value;
        var b = InkBounds(gray)!.Value;

        Assert.InRange(Math.Abs(a.Left - b.Left), 0, 2);
        Assert.InRange(Math.Abs(a.Right - b.Right), 0, 2);
        Assert.InRange(Math.Abs(a.Top - b.Top), 0, 2);
        Assert.InRange(Math.Abs(a.Bottom - b.Bottom), 0, 2);
        var ratio = (double)Darkness(gray) / Darkness(outline);
        Assert.InRange(ratio, 0.8, 1.4);

        var differing = 0;
        for (var y = 0; y < gray.Composer.PixelHeight; y++)
        for (var x = 0; x < gray.Composer.PixelWidth; x++)
            if (gray.Px(x, y) != outline.Px(x, y)) differing++;
        Assert.True(differing > 20, "the hinted run is not the outline run's pixels");
    }

    [Fact]
    public async Task GrayscaleGlyphs_AreTheSamePixelsAsTheSameTextDrawnAsAString()
    {
        using var glyphs = await SceneAsync(Win2dTextRendering.Grayscale);
        using var text = await SceneAsync(Win2dTextRendering.Grayscale);
        DrawRun(glyphs, PaintColor.Black);
        var font = text.Context.GetFont("Source Sans 3", 16, PaintFontStyle.Regular)!;
        var typeface = font.Typeface!;
        var baselineTop = 24 - 16 * typeface.Metrics.CellAscent / (double)typeface.Metrics.UnitsPerEm;
        text.Canvas.DrawString(Sample, font, PaintColor.Black, new PaintPoint(5, baselineTop), text.Canvas.MeasureString(Sample, font));

        var a = InkBounds(glyphs)!.Value;
        var b = InkBounds(text)!.Value;
        Assert.InRange(Math.Abs(a.Left - b.Left), 0, 1);
        Assert.InRange(Math.Abs(a.Right - b.Right), 0, 1);
        Assert.InRange((double)Darkness(glyphs) / Darkness(text), 0.97, 1.03);
    }

    [Fact]
    public async Task LcdGlyphs_HaveColourFringes_AndGrayscaleGlyphsHaveNone()
    {
        using var lcd = await SceneAsync(Win2dTextRendering.Lcd);
        using var gray = await SceneAsync(Win2dTextRendering.Grayscale);
        DrawRun(lcd, PaintColor.Black);
        DrawRun(gray, PaintColor.Black);

        static int Fringed(Scene scene)
        {
            var count = 0;
            for (var y = 0; y < scene.Composer.PixelHeight; y++)
            for (var x = 0; x < scene.Composer.PixelWidth; x++)
            {
                var p = scene.Px(x, y);
                if (Math.Abs(p.R - p.G) > 12 || Math.Abs(p.G - p.B) > 12) count++;
            }

            return count;
        }

        Assert.True(Fringed(lcd) > 30, "LCD text carries a coverage per stripe");
        Assert.Equal(0, Fringed(gray));
    }

    [Fact]
    public async Task OutlineMode_StillFillsOutlines()
    {
        using var scene = await SceneAsync(Win2dTextRendering.Outline);
        DrawRun(scene, PaintColor.Black);

        Assert.NotNull(InkBounds(scene));
    }

    [Fact]
    public async Task ARotatedRun_FallsBackToOutlinesAndStillDraws()
    {
        using var scene = await SceneAsync(Win2dTextRendering.Grayscale, 120, 120);
        var font = scene.Context.GetFont("Source Sans 3", 16, PaintFontStyle.Regular)!;
        scene.Canvas.PushTransform(Matrix3x2.CreateRotation(0.5f) * Matrix3x2.CreateTranslation(30, 20));
        scene.Canvas.DrawGlyphs(Place(scene, font, Sample, 0, 20), font, PaintColor.Black);
        scene.Canvas.PopTransform();

        Assert.NotNull(InkBounds(scene));
    }

    [Fact]
    public async Task ARunLargerThanAutoMasks_IsDrawnAsOutlines()
    {
        using var scene = await SceneAsync(Win2dTextRendering.Auto, 300, 120);
        var font = scene.Context.GetFont("Source Sans 3", 80, PaintFontStyle.Regular)!;
        scene.Canvas.DrawGlyphs(Place(scene, font, "Ag", 5, 80), font, PaintColor.Black);

        Assert.NotNull(InkBounds(scene));
    }

    [Fact]
    public async Task GlyphsScrolledPartlyOutOfTheTarget_AreCutAndNothingThrows()
    {
        using var scene = await SceneAsync(Win2dTextRendering.Lcd, 60, 40);
        DrawRun(scene, PaintColor.Black, x: -40);
        DrawRun(scene, PaintColor.Black, x: 40, baseline: 90);   // below the page entirely

        Assert.NotNull(InkBounds(scene));
    }

    [Fact]
    public async Task ColouredText_BlendsIntoTheBackgroundNotOverIt()
    {
        using var scene = await SceneAsync(Win2dTextRendering.Grayscale);
        DrawRun(scene, PaintColor.FromArgb(255, 200, 0, 0));

        var reddest = 0;
        for (var y = 0; y < scene.Composer.PixelHeight; y++)
        for (var x = 0; x < scene.Composer.PixelWidth; x++)
        {
            var p = scene.Px(x, y);
            if (p.R > 180 && p.G < 60 && p.B < 60) reddest++;
        }

        Assert.True(reddest > 30);
    }

    [Fact]
    public async Task InvisibleText_DrawsNothing()
    {
        using var scene = await SceneAsync(Win2dTextRendering.Grayscale);
        scene.Canvas.InvisibleText = true;
        DrawRun(scene, PaintColor.Black);

        Assert.Null(InkBounds(scene));
    }
}
