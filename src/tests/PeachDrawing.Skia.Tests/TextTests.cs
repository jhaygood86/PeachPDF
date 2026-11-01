using PeachDrawing.Core;
using PeachDrawing.Skia;
using PeachPDF.Tests.TestSupport;

namespace PeachDrawing.Skia.Tests;

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
        scene.Canvas.DrawString("Hello", SourceSans(scene), PaintColor.FromArgb(255, 255, 0, 0), new PaintPoint(5, 5), new Core.Size(50, 20));

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
        Assert.NotEmpty(crossings!);
    }

    [Fact]
    public async Task Letter_spacing_widens_the_drawn_run()
    {
        using var plain = await Scene.WithFontAsync();
        using var spaced = await Scene.WithFontAsync();
        var font = SourceSans(plain, 16);

        plain.Canvas.DrawString("iiii", font, PaintColor.FromArgb(255, 0, 0, 0), new PaintPoint(0, 5), new Core.Size(40, 20));
        spaced.Canvas.DrawString("iiii", SourceSans(spaced, 16), PaintColor.FromArgb(255, 0, 0, 0), new PaintPoint(0, 5), new Core.Size(40, 20), letterSpacing: 10);

        static int Rightmost(Scene s) => Enumerable.Range(0, 200).Last(x => Enumerable.Range(0, 60).Any(y => s.Px(x, y).A > 0));
        Assert.True(Rightmost(spaced) > Rightmost(plain) + 20);
    }

    [Fact]
    public async Task Glyphs_can_be_drawn_by_index()
    {
        using var scene = await Scene.WithFontAsync();
        var font = SourceSans(scene);
        var index = font.GetGlyphIndex(new System.Text.Rune('H'));
        Assert.NotEqual(0, index);

        scene.Canvas.DrawGlyphs([new GlyphPlacement(index, 10, 40)], font, PaintColor.FromArgb(255, 255, 0, 0));

        Assert.True(scene.OpaquePixels() > 20);
    }

    [Fact]
    public async Task A_colour_font_glyph_is_painted_in_its_own_colours()
    {
        using var scene = Scene.Create(60, 60);
        await using (var stream = File.OpenRead(BundledFonts.ColorV0))
            await scene.Context.AddFont(stream, "Color Test");

        var font = scene.Context.GetFont("Color Test", 40, PaintFontStyle.Regular)!;
        scene.Canvas.DrawString("A", font, PaintColor.FromArgb(255, 0, 0, 0), new PaintPoint(5, 5), new Core.Size(40, 40));

        Assert.True(scene.OpaquePixels() > 50);
    }

    [Fact]
    public async Task The_native_engine_measures_and_draws_with_skia_fonts()
    {
        using var scene = Scene.Create(200, 60, engine: SkiaTextEngine.Native);
        await using (var stream = File.OpenRead(BundledFonts.Ttf))
            await scene.Context.AddFont(stream, "Source Sans 3");

        var font = scene.Context.GetFont("Source Sans 3", 20, PaintFontStyle.Regular)!;
        Assert.IsType<NativeSkiaFont>(font);
        Assert.Null(font.Typeface);

        var size = scene.Canvas.MeasureString("Hello", font);
        Assert.InRange(size.Width, 30, 80);
        Assert.True(font.Height >= 20);
        Assert.True(font.HasGlyph(new System.Text.Rune('H')));
        Assert.Equal(5, scene.Canvas.CountShapedGlyphs("Hello", font));

        scene.Canvas.DrawString("Hello", font, PaintColor.FromArgb(255, 255, 0, 0), new PaintPoint(5, 5), size);
        Assert.Null(scene.Canvas.GetTextOutline("Hello", font, new PaintPoint(0, 30)));
        Assert.Null(scene.Canvas.GetInkCrossings("Hello", font, new PaintPoint(0, 0), 14, 16));
        Assert.True(scene.OpaquePixels() > 100);
    }

    [Fact]
    public async Task The_native_engine_fits_a_prefix_and_widens_with_letter_spacing()
    {
        using var scene = Scene.Create(200, 60, engine: SkiaTextEngine.Native);
        await using (var stream = File.OpenRead(BundledFonts.Ttf))
            await scene.Context.AddFont(stream, "Source Sans 3");
        var font = scene.Context.GetFont("Source Sans 3", 20, PaintFontStyle.Regular)!;

        var whole = scene.Canvas.MeasureString("Hello world", font).Width;
        scene.Canvas.MeasureString("Hello world", font, whole / 2, out var fit, out var fitWidth);

        Assert.InRange(fit, 1, 10);
        Assert.True(fitWidth <= whole / 2);

        scene.Canvas.DrawString("ii", font, PaintColor.FromArgb(255, 0, 0, 0), new PaintPoint(0, 5), new Core.Size(20, 20), letterSpacing: 30);
        Assert.Contains(Enumerable.Range(30, 20), x => Enumerable.Range(0, 60).Any(y => scene.Px(x, y).A > 0));
    }

    [Fact]
    public async Task A_native_font_without_a_bold_or_italic_face_is_synthesised()
    {
        using var scene = Scene.Create(200, 60, engine: SkiaTextEngine.Native);
        await using (var stream = File.OpenRead(BundledFonts.Ttf))
            await scene.Context.AddFont(stream, "Source Sans 3");

        var regular = scene.Context.GetFont("Source Sans 3", 20, PaintFontStyle.Regular)!;
        var styled = scene.Context.GetFont("Source Sans 3", 20, PaintFontStyle.Bold | PaintFontStyle.Italic)!;

        Assert.NotEqual(regular.FaceKey, styled.FaceKey);
        Assert.True(scene.Canvas.MeasureString("Hello", styled).Width > 0);
    }

    [Fact]
    public void A_native_context_lists_the_platforms_families()
    {
        using var context = new SkiaRenderContext(SkiaTextEngine.Native);

        Assert.Equal(SkiaTextEngine.Native, context.TextEngine);
        Assert.NotEmpty(SkiaSharp.SKFontManager.Default.FontFamilies);
    }
}

public class TextShaperSeamTests
{
    private sealed class CountingShaper : ISkiaTextShaper
    {
        public int Calls { get; private set; }

        public SkiaSharp.SKTypeface? Seen { get; private set; }

        public SkiaShapedRun Shape(string text, SkiaSharp.SKFont font)
        {
            Calls++;
            Seen = font.Typeface;
            return new SkiaShapedRun([1, 2], [0, 10], [0, 0], [0, 1], 20);
        }
    }

    private sealed class HarfBuzzShaper : ISkiaTextShaper, IDisposable
    {
        private readonly Dictionary<SkiaSharp.SKTypeface, SkiaSharp.HarfBuzz.SKShaper> _shapers = [];

        public SkiaShapedRun Shape(string text, SkiaSharp.SKFont font)
        {
            if (!_shapers.TryGetValue(font.Typeface, out var shaper))
                _shapers[font.Typeface] = shaper = new SkiaSharp.HarfBuzz.SKShaper(font.Typeface);

            var r = shaper.Shape(text, font);
            return new SkiaShapedRun(Array.ConvertAll(r.Codepoints, g => (ushort)g), r.Points.Select(p => p.X).ToArray(),
                r.Points.Select(p => p.Y).ToArray(), r.Clusters, r.Width);
        }

        public void Dispose()
        {
            foreach (var s in _shapers.Values)
                s.Dispose();
        }
    }

    private static async Task<(Scene Scene, Font Font)> NativeScene(ISkiaTextShaper? shaper)
    {
        var scene = new Scene(new SkiaRenderContext(SkiaTextEngine.Native, textShaper: shaper), 200, 60);
        await using var stream = File.OpenRead(PeachPDF.Tests.TestSupport.BundledFonts.Ttf);
        await scene.Context.AddFont(stream, "Source Sans 3");
        return (scene, scene.Context.GetFont("Source Sans 3", 20, PaintFontStyle.Regular)!);
    }

    [Fact]
    public async Task A_hosts_shaper_is_what_measures_and_draws_native_text()
    {
        var shaper = new CountingShaper();
        var (scene, font) = await NativeScene(shaper);
        using (scene)
        {
            Assert.Same(shaper, scene.Context.TextShaper);
            Assert.Equal(20, scene.Canvas.MeasureString("anything", font).Width);
            Assert.Equal(2, scene.Canvas.CountShapedGlyphs("anything", font));
            Assert.True(shaper.Calls >= 2);
            Assert.NotNull(shaper.Seen);
        }
    }

    [Fact]
    public async Task A_HarfBuzz_shaper_plugs_in_without_the_package_depending_on_it()
    {
        using var harfBuzz = new HarfBuzzShaper();
        var (scene, font) = await NativeScene(harfBuzz);
        using (scene)
        {
            var width = scene.Canvas.MeasureString("Hello", font).Width;
            scene.Canvas.DrawString("Hello", font, PaintColor.FromArgb(255, 255, 0, 0), new PaintPoint(5, 5), new Core.Size(width, 20));

            Assert.InRange(width, 30, 80);
            Assert.True(scene.OpaquePixels() > 100);
        }
    }

    [Fact]
    public async Task Without_a_shaper_the_basic_one_maps_each_character_to_a_glyph()
    {
        var (scene, font) = await NativeScene(null);
        using (scene)
        {
            Assert.Same(scene.Context.TextShaper, scene.Context.TextShaper);
            Assert.Equal(5, scene.Canvas.CountShapedGlyphs("Hello", font));
            Assert.Equal(6, scene.Canvas.CountShapedGlyphs("Hello\U0001F600", font));
        }
    }
}

/// <summary>The ready-made PeachDrawing.Text shaper, driving Skia's native fonts through the ISkiaTextShaper seam.</summary>
public class PeachTextShaperSeamTests
{
    [Fact]
    public async Task The_PeachDrawing_Text_shaper_shapes_native_skia_text()
    {
        using var scene = new Scene(new SkiaRenderContext(SkiaTextEngine.Native, textShaper: new PeachTextShaper()), 200, 60);
        await using (var stream = File.OpenRead(PeachPDF.Tests.TestSupport.BundledFonts.Ttf))
            await scene.Context.AddFont(stream, "Source Sans 3");
        var font = scene.Context.GetFont("Source Sans 3", 20, PaintFontStyle.Regular)!;

        var width = scene.Canvas.MeasureString("Hello", font).Width;
        scene.Canvas.DrawString("Hello", font, PaintColor.FromArgb(255, 255, 0, 0), new PaintPoint(5, 5), new Core.Size(width, 20));

        Assert.InRange(width, 30, 80);
        Assert.True(scene.OpaquePixels() > 100);
    }

    [Fact]
    public async Task It_applies_ligatures_the_basic_shaper_does_not()
    {
        using var peach = new Scene(new SkiaRenderContext(SkiaTextEngine.Native, textShaper: new PeachTextShaper()), 200, 60);
        using var basic = new Scene(new SkiaRenderContext(SkiaTextEngine.Native), 200, 60);
        foreach (var scene in new[] { peach, basic })
        {
            await using var stream = File.OpenRead(PeachPDF.Tests.TestSupport.BundledFonts.Ttf);
            await scene.Context.AddFont(stream, "Source Sans 3");
        }

        // Source Sans 3 has an fi ligature: shaped, two characters become one glyph.
        var shaped = peach.Canvas.CountShapedGlyphs("office", peach.Context.GetFont("Source Sans 3", 20, PaintFontStyle.Regular)!);
        var plain = basic.Canvas.CountShapedGlyphs("office", basic.Context.GetFont("Source Sans 3", 20, PaintFontStyle.Regular)!);

        Assert.Equal(6, plain);
        Assert.True(shaped < plain, $"{shaped} glyphs shaped, {plain} unshaped");
    }

    [Fact]
    public void A_font_whose_file_cannot_be_read_is_shaped_by_the_basic_shaper()
    {
        var shaper = new PeachTextShaper();
        using var font = new SkiaSharp.SKFont(SkiaSharp.SKTypeface.Default, 20);

        var run = shaper.Shape("abc", font);

        Assert.Equal(3, run.Glyphs.Length);
        Assert.True(run.Width > 0);
    }
}
