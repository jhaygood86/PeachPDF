using PeachDrawing.Core;
using PeachDrawing.Text;
using PeachDrawing.Text.Shaping;
using System.Buffers.Binary;
using System.Xml.Linq;
using static PeachDrawing.Svg.Tests.SvgScene;

namespace PeachDrawing.Svg.Tests;

public class SvgTextTests
{
    private static readonly SvgComposerOptions TextMode = new() { TextMode = SvgTextMode.Text };

    private static Size Measure(SvgScene scene, string text, Font font) => scene.Canvas.MeasureString(text, font);

    [Fact]
    public async Task OutlineTextIsAGroupOfInstancesOfExactGlyphOutlinesDefinedOnce()
    {
        using var scene = await SvgScene.WithFontAsync();
        var font = scene.Context.GetFont("Source Sans 3", 20, PaintFontStyle.Regular)!;
        scene.Canvas.DrawString("Hii", font, PaintColor.FromArgb(255, 10, 20, 30), new PaintPoint(5, 10), Measure(scene, "Hii", font));

        var group = Assert.Single(scene.Drawn());
        Assert.Equal(Ns + "g", group.Name);
        Assert.Equal("#0a141e", Attr(group, "fill"));
        Assert.Equal(string.Empty, Attr(group, "stroke"));
        var uses = group.Elements().ToList();
        Assert.Equal(3, uses.Count);
        Assert.Equal(Attr(uses[1], "href"), Attr(uses[2], "href")); // the same glyph is one definition
        Assert.Equal(2, scene.Defs().Count());               // H and i

        foreach (var use in uses)
        {
            var d = Attr(GlyphPath(scene, use), "d");
            Assert.StartsWith("M", d);
            Assert.Equal(d.Count(c => c == 'M'), d.Count(c => c == 'Z')); // every contour is closed
            Assert.DoesNotContain("NaN", d);
        }
    }

    [Fact]
    public async Task TheBaselineIsTheRunsTopPlusTheCellAscent()
    {
        using var scene = await SvgScene.WithFontAsync();
        var font = scene.Context.GetFont("Source Sans 3", 100, PaintFontStyle.Regular)!;
        scene.Canvas.DrawString("H", font, PaintColor.Black, new PaintPoint(0, 0), Measure(scene, "H", font));

        // The glyph's origin is on the baseline: the outline stands on y = 0 and rises above it.
        var use = Assert.Single(scene.Drawn());
        var typeface = font.Typeface!;
        Assert.Equal(100.0 * typeface.Metrics.CellAscent / typeface.Metrics.UnitsPerEm, double.Parse(Attr(use, "y")), 2);
        var ys = YCoordinates(Attr(GlyphPath(scene, use), "d"));
        Assert.Equal(0, ys.Max(), 2);
        Assert.True(ys.Min() < -50);
    }

    [Fact]
    public async Task SyntheticBoldStrokesTheOutlineAtTwoPercentOfTheEm()
    {
        using var scene = await SvgScene.WithFontAsync();
        var bold = scene.Context.GetFont("Source Sans 3", 50, PaintFontStyle.Bold)!;
        Assert.True((bold.SyntheticStyle & SyntheticStyle.Bold) != 0);
        scene.Canvas.DrawString("B", bold, PaintColor.Black, new PaintPoint(0, 0), Measure(scene, "B", bold));

        var use = Assert.Single(scene.Drawn());
        Assert.Equal("1", Attr(use, "stroke-width"));
        Assert.Equal("#000000", Attr(use, "stroke"));
        Assert.Equal("10", Attr(use, "stroke-miterlimit"));
    }

    [Fact]
    public async Task SyntheticItalicShearsTheOutlineAboutTheBaseline()
    {
        using var scene = await SvgScene.WithFontAsync();
        var regular = scene.Context.GetFont("Source Sans 3", 50, PaintFontStyle.Regular)!;
        var italic = scene.Context.GetFont("Source Sans 3", 50, PaintFontStyle.Italic)!;
        scene.Canvas.DrawString("I", regular, PaintColor.Black, new PaintPoint(0, 0), Measure(scene, "I", regular));
        scene.Canvas.DrawString("I", italic, PaintColor.Black, new PaintPoint(0, 0), Measure(scene, "I", italic));

        var (upright, slanted) = (XCoordinates(Attr(GlyphPath(scene, scene.Drawn().ElementAt(0)), "d")), XCoordinates(Attr(GlyphPath(scene, scene.Drawn().ElementAt(1)), "d")));
        Assert.True(slanted.Max() - upright.Max() > 5, "the top of an italic I leans right");
        Assert.Equal(upright.Min(), slanted.Min(), 0);
    }

    [Fact]
    public async Task LetterSpacingAddsToEachGlyphsAdvance()
    {
        using var scene = await SvgScene.WithFontAsync();
        var font = scene.Context.GetFont("Source Sans 3", 30, PaintFontStyle.Regular)!;
        scene.Canvas.DrawString("ll", font, PaintColor.Black, new PaintPoint(0, 0), Measure(scene, "ll", font));
        scene.Canvas.DrawString("ll", font, PaintColor.Black, new PaintPoint(0, 0), Measure(scene, "ll", font), letterSpacing: 10);

        double Gap(XElement run) => double.Parse(Attr(run.Elements().ElementAt(1), "x")) - double.Parse(Attr(run.Elements().ElementAt(0), "x"));
        Assert.Equal(Gap(scene.Drawn().ElementAt(0)) + 10, Gap(scene.Drawn().ElementAt(1)), 2);
    }

    [Fact]
    public async Task TextIsDrawnInLayoutUnitsSoTheSamePointSizeIsTwiceAsBigAtTwoUnitsToThePoint()
    {
        double Extent(SvgScene scene, double size)
        {
            var font = scene.Context.GetFont("Source Sans 3", size, PaintFontStyle.Regular)!;
            scene.Canvas.DrawString("H", font, PaintColor.Black, new PaintPoint(0, 0), scene.Canvas.MeasureString("H", font));
            var xs = XCoordinates(Attr(GlyphPath(scene, Assert.Single(scene.Drawn())), "d"));
            return xs.Max() - xs.Min();
        }

        using var one = await SvgScene.WithFontAsync();
        var scaled = new SvgRenderContext { PixelsPerPoint = 2 };
        await using (var stream = File.OpenRead(PeachPDF.Tests.TestSupport.BundledFonts.Ttf))
            await scaled.AddFont(stream, "Source Sans 3");
        using var two = new SvgScene(scaled);

        // 20 points is size 20 at one unit to the point and size 40 at two.
        Assert.Equal(2 * Extent(one, 20), Extent(two, 40), 1);
    }

    [Fact]
    public async Task InvisibleTextWritesNothingInOutlineMode()
    {
        using var scene = await SvgScene.WithFontAsync();
        var font = scene.Context.GetFont("Source Sans 3", 20, PaintFontStyle.Regular)!;
        scene.Canvas.InvisibleText = true;
        scene.Canvas.DrawString("Hi", font, PaintColor.Black, new PaintPoint(0, 0), Measure(scene, "Hi", font));

        Assert.Empty(scene.Drawn());
    }

    [Fact]
    public async Task PositionedGlyphsAreDrawnAtTheirOwnOrigins()
    {
        using var scene = await SvgScene.WithFontAsync();
        var font = scene.Context.GetFont("Source Sans 3", 40, PaintFontStyle.Regular)!;
        var typeface = font.Typeface!;
        typeface.TryMapRune(new System.Text.Rune('o'), out var glyph);
        scene.Canvas.DrawGlyphs([new GlyphPlacement(glyph, 100, 50), new GlyphPlacement(glyph, 200, 60)], font, PaintColor.Black);

        var uses = Assert.Single(scene.Drawn()).Elements().ToList();
        Assert.Equal([("100", "50"), ("200", "60")], uses.Select(u => (Attr(u, "x"), Attr(u, "y"))));
        Assert.Equal(Attr(uses[0], "href"), Attr(uses[1], "href"));
    }

    [Fact]
    public async Task TheTextOutlineIsAPathOfTheRunAtItsBaselineOrigin()
    {
        using var scene = await SvgScene.WithFontAsync();
        var font = scene.Context.GetFont("Source Sans 3", 40, PaintFontStyle.Regular)!;
        var outline = scene.Canvas.GetTextOutline("A", font, new PaintPoint(10, 60));

        Assert.NotNull(outline);
        var contours = outline!.GetCurveContours();
        Assert.NotEmpty(contours);
        Assert.All(contours.Select(c => c.Start.X), x => Assert.InRange(x, 10, 60));
    }

    [Fact]
    public async Task MeasuringAgreesWithTheLayoutEnginesOwnMeasurement()
    {
        using var scene = await SvgScene.WithFontAsync();
        var font = scene.Context.GetFont("Source Sans 3", 20, PaintFontStyle.Regular)!;
        var size = Measure(scene, "Hello", font);
        var expected = PeachDrawing.Core.TextMeasurement.Measure("Hello", font.Typeface!, font.Size, font.SyntheticStyle, ShapeSettings.Default);

        Assert.Equal(expected.Width, size.Width, 6);
        Assert.Equal(5, scene.Canvas.CountShapedGlyphs("Hello", font));
    }

    [Fact]
    public async Task MeasuringHowMuchFitsFindsTheLongestPrefixThatFits()
    {
        using var scene = await SvgScene.WithFontAsync();
        var font = scene.Context.GetFont("Source Sans 3", 20, PaintFontStyle.Regular)!;
        var full = Measure(scene, "abcdef", font).Width;
        var three = Measure(scene, "abc", font).Width;

        scene.Canvas.MeasureString("abcdef", font, three + 0.01, out var fit, out var fitWidth);
        Assert.Equal(3, fit);
        Assert.Equal(three, fitWidth, 6);

        scene.Canvas.MeasureString("abcdef", font, full + 1, out fit, out _);
        Assert.Equal(6, fit);

        scene.Canvas.MeasureString("abcdef", font, 0.5, out fit, out fitWidth);
        Assert.Equal((0, 0.0), (fit, fitWidth));
    }

    [Fact]
    public async Task InkCrossingsOfADescenderBandAreFoundForAGlyphThatDescends()
    {
        using var scene = await SvgScene.WithFontAsync();
        var font = scene.Context.GetFont("Source Sans 3", 40, PaintFontStyle.Regular)!;
        var typeface = font.Typeface!;
        var baseline = 40.0 * typeface.Metrics.CellAscent / typeface.Metrics.UnitsPerEm;

        var crossings = scene.Canvas.GetInkCrossings("gp", font, new PaintPoint(0, 0), baseline + 2, baseline + 4);
        Assert.NotNull(crossings);
        Assert.NotEmpty(crossings!);
        Assert.Null(scene.Canvas.GetInkCrossings("gp", font, new PaintPoint(0, 0), 5, 5));
    }

    // ---- selectable text -------------------------------------------------------------------------------

    [Fact]
    public async Task TextModeWritesSelectableTextWithEachCharacterAtItsShapedPosition()
    {
        using var scene = await SvgScene.WithFontAsync(options: TextMode);
        var font = scene.Context.GetFont("Source Sans 3", 20, PaintFontStyle.Regular)!;
        scene.Canvas.DrawString("Hello world", font, PaintColor.FromArgb(255, 25, 25, 112), new PaintPoint(5, 10), Measure(scene, "Hello world", font));

        var text = Assert.Single(scene.Drawn());
        Assert.Equal(Ns + "text", text.Name);
        Assert.Equal("Hello world", text.Value);
        Assert.Equal("#191970", Attr(text, "fill"));
        Assert.Equal("20", Attr(text, "font-size"));

        var xs = Attr(text, "x").Split(' ').Select(double.Parse).ToList();
        Assert.Equal(11, xs.Count);
        Assert.Equal(5, xs[0]);
        Assert.True(xs.Zip(xs.Skip(1), (a, b) => b > a).All(ok => ok), "positions increase");
        Assert.Contains("font-kerning:none", Attr(text, "style"));
        Assert.Contains("white-space:pre", Attr(text, "style"));
    }

    [Fact]
    public async Task TextModeEmbedsASubsetFontTheBrowserCanLoadHoldingOnlyTheGlyphsUsed()
    {
        using var scene = await SvgScene.WithFontAsync(options: TextMode);
        var font = scene.Context.GetFont("Source Sans 3", 20, PaintFontStyle.Regular)!;
        scene.Canvas.DrawString("abc", font, PaintColor.Black, new PaintPoint(0, 0), Measure(scene, "abc", font));

        var svg = scene.Document;
        var style = Assert.Single(svg.Root!.Elements(Ns + "style")).Value;
        var family = Attr(Assert.Single(scene.Drawn()), "font-family");
        Assert.Contains($"font-family:\"{family}\"", style);

        var base64 = style[(style.IndexOf("base64,", StringComparison.Ordinal) + 7)..style.IndexOf(')', style.IndexOf("base64,", StringComparison.Ordinal))];
        var bytes = Convert.FromBase64String(base64);
        var tables = SfntTables(bytes);

        // A browser needs these tables; the subset a PDF embeds has only the glyph ones.
        foreach (var required in new[] { "cmap", "head", "hhea", "hmtx", "maxp", "name", "OS/2", "post", "glyf", "loca" })
            Assert.Contains(required, tables);

        Assert.True(bytes.Length < new FileInfo(PeachPDF.Tests.TestSupport.BundledFonts.Ttf).Length / 2, "only the glyphs used are embedded");
        Assert.Equal(0xB1B0AFBAu, Checksum(bytes)); // head.checkSumAdjustment makes the whole file sum to the magic number
    }

    [Fact]
    public async Task AFontIsEmbeddedOnceHoweverManyRunsUseIt()
    {
        using var scene = await SvgScene.WithFontAsync(options: TextMode);
        var font = scene.Context.GetFont("Source Sans 3", 20, PaintFontStyle.Regular)!;
        scene.Canvas.DrawString("one", font, PaintColor.Black, new PaintPoint(0, 0), Measure(scene, "one", font));
        scene.Canvas.DrawString("two", font, PaintColor.Black, new PaintPoint(0, 30), Measure(scene, "two", font));

        var style = Assert.Single(scene.Document.Root!.Elements(Ns + "style")).Value;
        Assert.Equal(1, style.Split("@font-face").Length - 1);
        Assert.Equal(2, scene.Drawn().Count(e => e.Name == Ns + "text"));
    }

    [Fact]
    public async Task TextModeFallsBackToOutlinesForARunTheViewerCouldNotReproduce()
    {
        using var scene = await SvgScene.WithFontAsync(options: TextMode);
        var font = scene.Context.GetFont("Source Sans 3", 20, PaintFontStyle.Regular)!;
        // "ffi" is one ligature glyph: three characters, one glyph, so per-character positions cannot say where it goes.
        Assert.True(scene.Canvas.CountShapedGlyphs("ffi", font) < 3, "the bundled font ligates ffi");
        scene.Canvas.DrawString("ffi", font, PaintColor.Black, new PaintPoint(0, 0), Measure(scene, "ffi", font));

        Assert.Contains(Assert.Single(scene.Drawn()).Name, new[] { Ns + "g", Ns + "use" }); // glyph outlines, not <text>
        Assert.Empty(scene.Document.Root!.Elements(Ns + "style"));
    }

    [Fact]
    public async Task TextModeKeepsSyntheticStylesAndInvisibleTextSelectable()
    {
        using var scene = await SvgScene.WithFontAsync(options: TextMode);
        var bold = scene.Context.GetFont("Source Sans 3", 20, PaintFontStyle.Bold | PaintFontStyle.Italic)!;
        scene.Canvas.DrawString("ab", bold, PaintColor.Black, new PaintPoint(0, 0), Measure(scene, "ab", bold));
        scene.Canvas.InvisibleText = true;
        scene.Canvas.DrawString("ab", bold, PaintColor.Black, new PaintPoint(0, 30), Measure(scene, "ab", bold));

        var texts = scene.Drawn().ToList();
        Assert.Equal("0.4", Attr(texts[0], "stroke-width"));
        Assert.StartsWith("matrix(1 0 -0.34202", Attr(texts[0], "transform"));
        Assert.Equal("0", Attr(texts[1], "fill-opacity"));
        Assert.Equal(string.Empty, Attr(texts[1], "fill"));
    }

    [Fact]
    public async Task TextWithMarkupCharactersIsEscaped()
    {
        using var scene = await SvgScene.WithFontAsync(options: TextMode);
        var font = scene.Context.GetFont("Source Sans 3", 20, PaintFontStyle.Regular)!;
        scene.Canvas.DrawString("a<b&c", font, PaintColor.Black, new PaintPoint(0, 0), Measure(scene, "a<b&c", font));

        Assert.Equal("a<b&c", Assert.Single(scene.Drawn()).Value); // parsed back from escaped text
    }

    // ---- helpers ---------------------------------------------------------------------------------------

    /// <summary>The definition a glyph instance (<c>&lt;use&gt;</c>) refers to.</summary>
    private static XElement GlyphPath(SvgScene scene, XElement use) =>
        scene.Defs().Single(d => "#" + Attr(d, "id") == Attr(use.Name == Ns + "use" ? use : use.Elements().First(), "href"));

    private static List<double> XCoordinates(string d) => Coordinates(d, 0);

    private static List<double> YCoordinates(string d) => Coordinates(d, 1);

    /// <summary>The x (<paramref name="axis"/> 0) or y (1) of every point in M/L/C path data, which is always pairs of numbers.</summary>
    private static List<double> Coordinates(string d, int axis)
    {
        var numbers = System.Text.RegularExpressions.Regex.Matches(d, @"-?\d+(\.\d+)?").Select(m => double.Parse(m.Value)).ToList();
        return numbers.Where((_, i) => i % 2 == axis).ToList();
    }

    private static HashSet<string> SfntTables(byte[] font)
    {
        var count = BinaryPrimitives.ReadUInt16BigEndian(font.AsSpan(4));
        return Enumerable.Range(0, count).Select(i => System.Text.Encoding.ASCII.GetString(font, 12 + 16 * i, 4)).ToHashSet();
    }

    private static uint Checksum(byte[] data)
    {
        uint sum = 0;
        for (var i = 0; i + 3 < data.Length; i += 4)
            sum += BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(i));
        return sum;
    }
}
