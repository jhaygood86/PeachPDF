using PeachDrawing.Core;
using PeachDrawing.Core.Geometry;
using PeachPDF.Tests.TestSupport;
using System.Numerics;
using static PeachDrawing.Svg.Tests.SvgScene;

namespace PeachDrawing.Svg.Tests;

/// <summary>The less common paths through the canvas: every blend mode, brushes that draw nothing, colour glyphs, fonts that cannot be embedded.</summary>
public class SvgDetailTests
{
    private static PaintColor C(int r, int g, int b, int a = 255) => PaintColor.FromArgb(a, r, g, b);

    [Theory]
    [InlineData(PaintBlendMode.Normal, "")]
    [InlineData(PaintBlendMode.Multiply, "multiply")]
    [InlineData(PaintBlendMode.Screen, "screen")]
    [InlineData(PaintBlendMode.Overlay, "overlay")]
    [InlineData(PaintBlendMode.Darken, "darken")]
    [InlineData(PaintBlendMode.Lighten, "lighten")]
    [InlineData(PaintBlendMode.ColorDodge, "color-dodge")]
    [InlineData(PaintBlendMode.ColorBurn, "color-burn")]
    [InlineData(PaintBlendMode.HardLight, "hard-light")]
    [InlineData(PaintBlendMode.SoftLight, "soft-light")]
    [InlineData(PaintBlendMode.Difference, "difference")]
    [InlineData(PaintBlendMode.Exclusion, "exclusion")]
    [InlineData(PaintBlendMode.Hue, "hue")]
    [InlineData(PaintBlendMode.Saturation, "saturation")]
    [InlineData(PaintBlendMode.Color, "color")]
    [InlineData(PaintBlendMode.Luminosity, "luminosity")]
    [InlineData(PaintBlendMode.Plus, "plus-lighter")]
    public void EveryBlendModeHasItsCssName(PaintBlendMode mode, string css)
    {
        using var scene = Create();
        scene.Canvas.PushBlendMode(mode);
        scene.Canvas.DrawRectangle(scene.Solid(0, 0, 0), 0, 0, 1, 1);
        scene.Canvas.PopBlendMode();

        Assert.Equal(css.Length == 0 ? string.Empty : "mix-blend-mode:" + css, Attr(Assert.Single(scene.Drawn()), "style"));
    }

    [Fact]
    public void SmoothingAndTaggingCallsAreAcceptedAndWriteNothing()
    {
        using var scene = Create();
        var g = scene.Canvas;
        var previous = g.SetAntiAliasSmoothingMode();
        g.ReturnPreviousSmoothingMode(previous);
        g.PushAntiAlias(true);
        g.PopAntiAlias();
        g.BeginMarkedContent("P", 1);
        g.EndMarkedContent();
        g.BeginArtifact();
        g.BeginVariableText();
        g.EndVariableText();
        g.SuspendClipping();
        g.ResumeClipping();

        Assert.Empty(scene.Drawn());
    }

    [Fact]
    public void ASquareCapIsWritten()
    {
        using var scene = Create();
        var pen = scene.Pen(0, 0, 0, 4);
        pen.LineCap = LineCap.Square;
        scene.Canvas.DrawLine(pen, 0, 0, 10, 0);

        Assert.Equal("square", Attr(Assert.Single(scene.Drawn()), "stroke-linecap"));
    }

    [Fact]
    public void AHatchPenStrokesWithItsPattern()
    {
        using var scene = Create();
        var pen = scene.Context.GetPen(new HatchBrush(HatchStyle.Horizontal, C(0, 0, 0), C(255, 255, 255), 6, 1));
        pen.Width = 4;
        scene.Canvas.DrawLine(pen, 0, 0, 20, 0);

        var line = scene.Drawn().Single();
        var pattern = scene.Defs().Single(e => e.Name == Ns + "pattern");
        Assert.Equal($"url(#{Attr(pattern, "id")})", Attr(line, "stroke"));
    }

    [Fact]
    public void ABrushThatPaintsNothingDrawsNothing()
    {
        using var scene = Create();
        var empty = Array.Empty<(PaintColor PaintColor, double Position)>();
        scene.Canvas.DrawRectangle(scene.Context.GetLinearGradientBrush(new PaintPoint(0, 0), new PaintPoint(1, 1), empty), 0, 0, 5, 5);
        scene.Canvas.DrawRectangle(scene.Context.GetRadialGradientBrush(new PaintPoint(0, 0), 1, 1, empty), 0, 0, 5, 5);
        scene.Canvas.DrawRectangle(scene.Context.GetConicGradientBrush(new PaintPoint(0, 0), 1, [], []), 0, 0, 5, 5);
        var pen = scene.Context.GetPen(scene.Context.GetLinearGradientBrush(new PaintPoint(0, 0), new PaintPoint(1, 1), empty));
        scene.Canvas.DrawLine(pen, 0, 0, 5, 5);

        Assert.Empty(scene.Drawn());
        Assert.Empty(scene.Defs());
    }

    [Fact]
    public void ARadialGradientWithBothABrushTransformAndAnEllipseWritesBothInOrder()
    {
        using var scene = Create();
        var brush = scene.Context.GetRadialGradientBrush(new PaintPoint(10, 10), 20, 10, [(C(0, 0, 0), 0), (C(255, 255, 255), 1)], false, null, 0,
            Matrix3x2.CreateTranslation(5, 5));
        scene.Canvas.DrawRectangle(brush, 0, 0, 50, 50);

        Assert.Equal("matrix(1 0 0 1 5 5) translate(10 10) scale(1 0.5) translate(-10 -10)", Attr(Assert.Single(scene.Defs()), "gradientTransform"));
    }

    [Fact]
    public void AConicGradientSkipsTransparentWedgesAndPadsBeyondItsFirstAndLastStops()
    {
        using var scene = Create();
        // Fully transparent up to a third of a turn, then green for the rest of the first half, red by the end.
        var conic = scene.Context.GetConicGradientBrush(new PaintPoint(10, 10), 10,
            [C(0, 0, 0, 0), C(0, 0, 0, 0), C(0, 255, 0), C(255, 0, 0)], [0, Math.PI / 3, Math.PI, Math.PI * 1.5]);
        scene.Canvas.DrawRectangle(conic, 0, 0, 20, 20);

        var wedges = Assert.Single(scene.Drawn()).Elements().ToList();
        Assert.True(wedges.Count < 360, "wedges of a transparent stretch are not written");
        Assert.True(wedges.Count > 100);
        Assert.Equal("#ff0000", Attr(wedges[^1], "fill")); // past the last stop is the last colour
    }

    [Fact]
    public void ScaleFactorsKeepTheirPrecisionWhileCoordinatesAreRounded()
    {
        using var scene = Create();
        var image = scene.Png(4032 / 504); // 8 pixels
        scene.Canvas.DrawImage(image, new Rect(0, 0, 0.397, 0.397)); // a scale of 0.04963

        Assert.StartsWith("matrix(0.0496", Attr(Assert.Single(scene.Drawn()), "transform"));
        Assert.Equal("0.0496", SvgDefs.Factor(0.0496));
        Assert.Equal("0.05", new SvgDefs(3).Num(0.0496));
    }

    [Fact]
    public void AConicFillOfAnEvenOddShapeClipsWithTheEvenOddRule()
    {
        using var scene = Create();
        var ring = scene.Canvas.GetGraphicsPath();
        ring.FillMode = FillMode.EvenOdd;
        ring.AddCircle(20, 20, 15);
        ring.AddCircle(20, 20, 5);
        var conic = scene.Context.GetConicGradientBrush(new PaintPoint(20, 20), 15, [C(255, 0, 0), C(0, 0, 255)], [0, Math.PI * 2]);
        scene.Canvas.DrawPath(conic, ring);

        var clip = Assert.Single(scene.Defs());
        Assert.Equal("evenodd", Attr(Assert.Single(clip.Elements()), "clip-rule"));
        Assert.Equal(string.Empty, Attr(Assert.Single(clip.Elements()), "fill-rule"));
    }

    [Fact]
    public void ALayerColourMatrixWithNoBoundsUsesARegionOfEverything()
    {
        using var scene = Create();
        var invert = new ColorMatrix(new Matrix4x4(-1, 0, 0, 0, 0, -1, 0, 0, 0, 0, -1, 0, 0, 0, 0, 1), new Vector4(1, 1, 1, 0));
        using (var layer = scene.Canvas.BeginLayer(new LayerOptions(ColorMatrix: invert)))
        {
            layer!.Canvas.DrawRectangle(scene.Solid(0, 0, 0), 0, 0, 5, 5);
        }

        var filter = Assert.Single(scene.Defs());
        Assert.Equal("userSpaceOnUse", Attr(filter, "filterUnits"));
        Assert.Equal("-1000000", Attr(filter, "x")); // everything: a stroke or a lone line is not cut off
        Assert.Equal("sRGB", Attr(filter, "color-interpolation-filters"));
    }

    [Fact]
    public void ALayerColourMatrixEffectIsAFeColorMatrixAfterTheOthers()
    {
        using var scene = Create();
        var identity = new ColorMatrix(Matrix4x4.Identity, Vector4.Zero);
        using (var layer = scene.Canvas.BeginLayer(new LayerOptions(Bounds: new Rect(0, 0, 10, 10), Effects: [new BlurEffect(1), new ColorMatrixEffect(identity)])))
        {
            layer!.Canvas.DrawRectangle(scene.Solid(0, 0, 0), 0, 0, 5, 5);
        }

        var primitives = scene.Defs().Single(e => e.Name == Ns + "filter").Elements().Select(e => e.Name.LocalName).ToList();
        Assert.Equal(["feGaussianBlur", "feColorMatrix"], primitives);
    }

    [Fact]
    public void AClosedLayerIsNotClosedAgainByASecondDisposeOrAfterThePageIsWritten()
    {
        using var scene = Create();
        var layer = scene.Canvas.BeginLayer(new LayerOptions(Opacity: 0.5))!;
        layer.Canvas.DrawRectangle(scene.Solid(0, 0, 0), 0, 0, 1, 1);
        layer.Dispose();
        layer.Dispose();
        var layerLeftOpen = scene.Canvas.BeginLayer(new LayerOptions(Opacity: 0.5))!;
        _ = scene.Composer.ToSvg();
        layerLeftOpen.Dispose(); // the page is already written: nothing to close, and nothing thrown

        Assert.Single(scene.Drawn(), e => e.Name == Ns + "g"); // the second layer held nothing, so saving the page took it back out
    }

    [Fact]
    public void ClippingToARectangleKeepsTheShapeAndOnlyTheShapeInsideIt()
    {
        var path = new SvgGraphicsPath();
        path.AddCircle(10, 10, 10);
        var clipped = path.ClipToRect(new Rect(0, 0, 10, 20));

        var contours = clipped.GetCurveContours();
        Assert.NotEmpty(contours);
        Assert.All(contours.SelectMany(c => c.Commands.Select(x => x.End).Append(c.Start)), p => Assert.InRange(p.X, -1e-6, 10 + 1e-6));
        Assert.Equal(path.FillMode, clipped.FillMode);
        Assert.Empty(path.ClipToRect(new Rect(100, 100, 1, 1)).GetCurveContours());
        clipped.Dispose();
    }

    [Fact]
    public void ATileImageIsNotSmoothedOrCrispAndReportsItsSize()
    {
        using var scene = Create();
        var (_, image) = scene.Canvas.CreateTile(12, 6)!.Value;

        Assert.Equal((12.0, 6.0), (image.Width, image.Height));
        image.Interpolate = false;
        Assert.False(image.Interpolate);
        scene.Canvas.DrawImage(image, new Rect(0, 0, 12, 6), ImageSampling.Nearest);
        Assert.Equal(string.Empty, Attr(Assert.Single(scene.Drawn()), "style")); // a tile is vector content: nothing to keep crisp
    }

    [Fact]
    public void ImageSignaturesAreRecognisedAndTheOthersAreNot()
    {
        Assert.Equal("image/png", SvgImage.Sniff([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]));
        Assert.Equal("image/jpeg", SvgImage.Sniff([0xFF, 0xD8, 0xFF, 0xE0]));
        Assert.Equal("image/gif", SvgImage.Sniff("GIF89a"u8));
        Assert.Equal("image/webp", SvgImage.Sniff("RIFF\0\0\0\0WEBPVP8 "u8));
        Assert.Equal("image/avif", SvgImage.Sniff("\0\0\0\0ftypavif"u8));
        Assert.Null(SvgImage.Sniff("BM\0\0\0\0\0\0\0\0\0\0"u8));
        Assert.Null(SvgImage.Sniff([]));
    }

    [Fact]
    public void ATranslucentImageIsDecodedPremultiplied()
    {
        using var scene = Create();
        var raster = new RasterRenderContext();
        using var canvas = raster.CreateCanvas(2, 2);
        canvas.DrawRectangle(raster.GetSolidBrush(C(200, 100, 0, 128)), 0, 0, 2, 2);
        using var png = new MemoryStream();
        canvas.Save(png, "png", new PeachImage.Formats.Png.PngEncoderOptions());

        // Read through a stream that cannot hand over its buffer: the decoder must copy it.
        var image = scene.Context.ImageFromStream(new OneWayStream(png.ToArray()));
        var pixels = image.GetPixels()!.Value.PremultipliedRgba.Span;

        Assert.Equal(128, pixels[3]);
        Assert.InRange(pixels[0], 99, 101); // 200 * 128 / 255
        Assert.InRange(pixels[1], 49, 51);
    }

    [Fact]
    public void ControlCharactersAreDroppedAndMarkupIsEscapedAndNonFiniteNumbersAreZero()
    {
        Assert.Equal("a&amp;b&lt;c&gt;d&quot;e", SvgDefs.Escape("a&b<c>d\"e"));
        Assert.Equal("ab", SvgDefs.Escape("a\u0001b"));
        Assert.Equal("a\tb", SvgDefs.Escape("a\tb"));
        var defs = new SvgDefs(3);
        Assert.Equal("0", defs.Num(double.NaN));
        Assert.Equal("0", defs.Num(double.PositiveInfinity));
        Assert.Equal("0", defs.Num(-0.0001));
    }

    // ---- fonts and text -----------------------------------------------------------------------------

    [Fact]
    public async Task ColourGlyphsAreVectorLayersInTheirPaletteColours()
    {
        var context = new SvgRenderContext();
        await using var stream = File.OpenRead(BundledFonts.ColorV0);
        await context.AddFont(stream, "Color Test");
        using var scene = new SvgScene(context);
        var font = context.GetFont("Color Test", 40, PaintFontStyle.Regular)!;

        // 'A' is a red box under a green triangle.
        scene.Canvas.DrawString("A", font, PaintColor.Black, new PaintPoint(0, 0), scene.Canvas.MeasureString("A", font));

        var fills = scene.Drawn().SelectMany(e => e.DescendantsAndSelf()).Select(e => Attr(e, "fill")).Where(f => f.Length > 0).ToList();
        Assert.True(fills.Distinct().Count() >= 2, "the layers have different colours: " + string.Join(",", fills));
        Assert.DoesNotContain(scene.Drawn(), e => e.Name == Ns + "image");
    }

    [Fact]
    public async Task APaletteOverrideRecoloursALayer()
    {
        var context = new SvgRenderContext();
        await using var stream = File.OpenRead(BundledFonts.ColorV0);
        await context.AddFont(stream, "Color Test");
        using var plain = new SvgScene(context);
        using var recoloured = new SvgScene(context);
        var font = context.GetFont("Color Test", 40, PaintFontStyle.Regular)!;
        var palette = new FontPalette(0, [new KeyValuePair<int, PaintColor>(0, C(255, 0, 255))]);

        plain.Canvas.DrawString("A", font, PaintColor.Black, new PaintPoint(0, 0), plain.Canvas.MeasureString("A", font));
        recoloured.Canvas.DrawString("A", font, PaintColor.Black, new PaintPoint(0, 0), recoloured.Canvas.MeasureString("A", font), fontPalette: palette);

        Assert.DoesNotContain("#ff00ff", plain.Composer.ToSvg());
        Assert.Contains("#ff00ff", recoloured.Composer.ToSvg());
    }

    [Fact]
    public async Task ColourGlyphsOfAVersionOneFontCarryTheirGradients()
    {
        var context = new SvgRenderContext();
        await using var stream = File.OpenRead(BundledFonts.ColorV1);
        await context.AddFont(stream, "Color Test 1");
        using var scene = new SvgScene(context);
        var font = context.GetFont("Color Test 1", 40, PaintFontStyle.Regular)!;
        scene.Canvas.DrawString("G", font, PaintColor.Black, new PaintPoint(0, 0), scene.Canvas.MeasureString("G", font));

        Assert.Contains(scene.Defs(), e => e.Name == Ns + "linearGradient");
    }

    [Fact]
    public async Task AFontThatCannotBeEmbeddedAsASubsetIsWrittenAsOutlinesInTextMode()
    {
        // A CFF font is handed over whole by the exporter, which a document must not embed for a few glyphs.
        var context = new SvgRenderContext();
        await using var stream = File.OpenRead(BundledFonts.Otf);
        await context.AddFont(stream, "Code Test");
        using var scene = new SvgScene(context, options: new SvgComposerOptions { TextMode = SvgTextMode.Text });
        var font = context.GetFont("Code Test", 20, PaintFontStyle.Regular)!;
        scene.Canvas.DrawString("abc", font, PaintColor.Black, new PaintPoint(0, 0), scene.Canvas.MeasureString("abc", font));

        Assert.DoesNotContain(scene.Drawn(), e => e.Name == Ns + "text");
        Assert.Empty(scene.Document.Root!.Elements(Ns + "style"));
    }

    [Fact]
    public async Task TranslucentTextKeepsItsOpacityOnFillAndBoldStroke()
    {
        using var scene = await SvgScene.WithFontAsync();
        var bold = scene.Context.GetFont("Source Sans 3", 30, PaintFontStyle.Bold)!;
        scene.Canvas.DrawString("ab", bold, C(0, 0, 0, 128), new PaintPoint(0, 0), scene.Canvas.MeasureString("ab", bold));

        var group = Assert.Single(scene.Drawn());
        Assert.Equal("0.502", Attr(group, "fill-opacity"));
        Assert.Equal("0.502", Attr(group, "stroke-opacity"));

        using var text = await SvgScene.WithFontAsync(options: new SvgComposerOptions { TextMode = SvgTextMode.Text });
        var regular = text.Context.GetFont("Source Sans 3", 30, PaintFontStyle.Regular)!;
        text.Canvas.DrawString("ab", regular, C(0, 0, 0, 128), new PaintPoint(0, 0), text.Canvas.MeasureString("ab", regular));
        Assert.Equal("0.502", Attr(Assert.Single(text.Drawn()), "fill-opacity"));
    }

    [Fact]
    public async Task TheFontsMetricsAreTheTypefacesScaledToLayoutUnits()
    {
        var context = new SvgRenderContext { PixelsPerPoint = 2 };
        await using var stream = File.OpenRead(BundledFonts.Ttf);
        await context.AddFont(stream, "Metrics Test");
        var font = context.GetFont("Metrics Test", 40, PaintFontStyle.Regular)!; // 20 points
        var typeface = font.Typeface!;
        var upem = typeface.Metrics.UnitsPerEm;

        Assert.Equal(20, font.Size);
        Assert.Equal(20.0 * typeface.Metrics.LineSpacing / upem * 2, font.Height, 6);
        Assert.Equal(Math.Round(20.0 * typeface.Metrics.CellAscent / upem * 2), font.Ascent, 6);
        Assert.Equal(20.0 * typeface.Metrics.CellAscent / upem * 2, font.TextBaselineOffset, 6);
        Assert.True(font.NormalLineHeight > 0);
        Assert.True(font.UnderlineOffset > 0);
        Assert.True(font.UnderlineThickness >= 0);
        _ = font.UnderlinePosition;
        Assert.Equal(font.Height / 6, font.LeftPadding, 6);
        Assert.True(font.HasGlyph(new System.Text.Rune('a')));
        Assert.False(font.HasGlyph(new System.Text.Rune(0x4E2D)));
        _ = font.MatchesEmojiPresentation(new System.Text.Rune('a'), PeachDrawing.Text.Unicode.EmojiPresentation.Text);
        Assert.NotEmpty(font.FaceKey);
        Assert.Equal(font.FaceKey, font.FaceKey);
        Assert.True(font.GetWhitespaceWidth(new SvgScene(context).Canvas) > 0);
        Assert.NotEqual(0, font.GetGlyphIndex(new System.Text.Rune('a')));
        Assert.True(font.GetGlyphAdvanceWidthDesignUnits(font.GetGlyphIndex(new System.Text.Rune('a'))) > 0);
        Assert.Equal(upem, font.FontUnitsPerEm);
        Assert.NotNull(font.XHeightEm);
        Assert.NotNull(font.CapHeightEm);
        _ = font.GetBaselineHeightEm("ideo", vertical: false);
        _ = font.SupportsFontVariantCaps(PeachDrawing.Text.Shaping.CapsMode.SmallCaps);
        _ = font.SupportsFontVariantPosition(PeachDrawing.Text.Shaping.SubSuperMode.Super);
        _ = font.GetSubSuperscriptMetrics(superscript: true);
        Assert.False(font.HasMathTable);
        Assert.Null(font.MathTable);
        Assert.Equal(0, font.PaletteCount);
        Assert.Equal(0, font.PaletteEntryCount);
        Assert.Null(font.FirstLightPalette());
        Assert.Null(font.FirstDarkPalette());
        Assert.False(font.TryGetPaletteColor(0, 0, out _));
        _ = font.HasVerticalMetrics;
        _ = font.HasVerticalOrigin;
        Assert.True(font.GetVerticalAdvance(new System.Text.Rune('a')) > 0);
        _ = font.GetVerticalOriginY(new System.Text.Rune('a'));
    }

    [Fact]
    public async Task AColourFontExposesItsPalettes()
    {
        var context = new SvgRenderContext();
        await using var stream = File.OpenRead(BundledFonts.ColorV0);
        await context.AddFont(stream, "Palette Test");
        var font = context.GetFont("Palette Test", 20, PaintFontStyle.Regular)!;

        Assert.True(font.PaletteCount >= 1);
        Assert.True(font.PaletteEntryCount >= 1);
        Assert.True(font.TryGetPaletteColor(0, 0, out var color));
        Assert.True(color.A > 0);
        _ = font.FirstLightPalette();
        _ = font.FirstDarkPalette();
    }

    [Fact]
    public async Task AMathFontExposesItsMathTable()
    {
        var context = new SvgRenderContext();
        await using var stream = File.OpenRead(BundledFonts.Math);
        await context.AddFont(stream, "Math Test");
        var font = context.GetFont("Math Test", 20, PaintFontStyle.Regular)!;

        Assert.True(font.HasMathTable);
        Assert.NotNull(font.MathTable);
    }

    [Fact]
    public async Task AFallbackFontIsFoundForACodepointOnlyAnAddedFontCovers()
    {
        var context = new SvgRenderContext();
        await using var stream = File.OpenRead(BundledFonts.Cjk);
        await context.AddFont(stream, "Cjk Test");

        var fallback = context.GetSystemFallbackFontForCodepoint(20, PaintFontStyle.Regular, new System.Text.Rune(0x4E2D));
        Assert.NotNull(fallback);
        Assert.True(fallback!.HasGlyph(new System.Text.Rune(0x4E2D)));
    }

    private sealed class OneWayStream(byte[] data) : Stream
    {
        private readonly MemoryStream _inner = new(data, writable: false);

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
