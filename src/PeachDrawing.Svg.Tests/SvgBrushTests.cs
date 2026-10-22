using PeachDrawing.Core;
using PeachDrawing.Core.Geometry;
using System.Numerics;
using System.Xml.Linq;
using static PeachDrawing.Svg.Tests.SvgScene;

namespace PeachDrawing.Svg.Tests;

public class SvgBrushTests
{
    private static PaintColor C(int r, int g, int b, int a = 255) => PaintColor.FromArgb(a, r, g, b);

    private static string UrlId(XElement element, string attribute = "fill")
    {
        var value = Attr(element, attribute);
        Assert.StartsWith("url(#", value);
        return value[5..^1];
    }

    [Fact]
    public void ALinearGradientIsAUserSpaceGradientWithItsStopsAndTheShapeReferencesIt()
    {
        using var scene = Create();
        var brush = scene.Context.GetLinearGradientBrush(new PaintPoint(10, 20), new PaintPoint(110, 20),
            [(C(255, 0, 0), 0), (C(0, 0, 255, 51), 1)]);
        scene.Canvas.DrawRectangle(brush, 0, 0, 100, 50);

        var gradient = Assert.Single(scene.Defs());
        Assert.Equal(Ns + "linearGradient", gradient.Name);
        Assert.Equal("userSpaceOnUse", Attr(gradient, "gradientUnits"));
        Assert.Equal(("10", "20", "110", "20"), (Attr(gradient, "x1"), Attr(gradient, "y1"), Attr(gradient, "x2"), Attr(gradient, "y2")));
        Assert.Equal(string.Empty, Attr(gradient, "spreadMethod"));

        var stops = gradient.Elements().ToList();
        Assert.Equal(("0", "#ff0000", ""), (Attr(stops[0], "offset"), Attr(stops[0], "stop-color"), Attr(stops[0], "stop-opacity")));
        Assert.Equal(("1", "#0000ff", "0.2"), (Attr(stops[1], "offset"), Attr(stops[1], "stop-color"), Attr(stops[1], "stop-opacity")));
        Assert.Equal(Attr(gradient, "id"), UrlId(Assert.Single(scene.Drawn())));
    }

    [Fact]
    public void ARepeatingGradientSaysSoAndTheSameGradientIsDefinedOnce()
    {
        using var scene = Create();
        for (var i = 0; i < 3; i++)
        {
            var brush = scene.Context.GetLinearGradientBrush(new PaintPoint(0, 0), new PaintPoint(10, 10), [(C(0, 0, 0), 0), (C(255, 255, 255), 1)], isRepeating: true);
            scene.Canvas.DrawRectangle(brush, 0, i * 10, 10, 10);
        }

        Assert.Equal("repeat", Attr(Assert.Single(scene.Defs()), "spreadMethod"));
        Assert.Equal(3, scene.Drawn().Count());
    }

    [Fact]
    public void ACircularRadialGradientHasACentreARadiusAndAFocus()
    {
        using var scene = Create();
        var brush = scene.Context.GetRadialGradientBrush(new PaintPoint(50, 40), 30, 30, [(C(255, 255, 255), 0), (C(0, 0, 0), 1)], focalCenter: new PaintPoint(45, 35));
        scene.Canvas.DrawRectangle(brush, 0, 0, 100, 100);

        var gradient = Assert.Single(scene.Defs());
        Assert.Equal(Ns + "radialGradient", gradient.Name);
        Assert.Equal(("50", "40", "30", "45", "35"), (Attr(gradient, "cx"), Attr(gradient, "cy"), Attr(gradient, "r"), Attr(gradient, "fx"), Attr(gradient, "fy")));
        Assert.Equal(string.Empty, Attr(gradient, "gradientTransform"));
    }

    [Fact]
    public void AnEllipticalRadialGradientIsACircleSquashedAboutItsCentreWithTheFocusStretchedBack()
    {
        using var scene = Create();
        var brush = scene.Context.GetRadialGradientBrush(new PaintPoint(50, 40), 40, 20, [(C(255, 255, 255), 0), (C(0, 0, 0), 1)], focalCenter: new PaintPoint(50, 30));
        scene.Canvas.DrawRectangle(brush, 0, 0, 100, 100);

        var gradient = Assert.Single(scene.Defs());
        Assert.Equal("40", Attr(gradient, "r"));
        Assert.Equal("translate(50 40) scale(1 0.5) translate(-50 -40)", Attr(gradient, "gradientTransform"));
        Assert.Equal("20", Attr(gradient, "fy")); // 40 + (30 - 40) * 40 / 20
    }

    [Fact]
    public void ARadialGradientsOwnTransformComesFirstAndAFocusRadiusIsKept()
    {
        using var scene = Create();
        var brush = scene.Context.GetRadialGradientBrush(new PaintPoint(0, 0), 10, 10, [(C(0, 0, 0), 0), (C(255, 255, 255), 1)], false, null, 2.5,
            Matrix3x2.CreateTranslation(30, 40));
        scene.Canvas.DrawRectangle(brush, 0, 0, 100, 100);

        var gradient = Assert.Single(scene.Defs());
        Assert.Equal("matrix(1 0 0 1 30 40)", Attr(gradient, "gradientTransform"));
        Assert.Equal("2.5", Attr(gradient, "fr"));
    }

    [Fact]
    public void AConicGradientIsAFanOfWedgesClippedToTheShape()
    {
        using var scene = Create();
        var conic = scene.Context.GetConicGradientBrush(new PaintPoint(50, 50), 40,
            [C(255, 0, 0), C(0, 255, 0), C(0, 0, 255), C(255, 0, 0)], [0, Math.PI * 2 / 3, Math.PI * 4 / 3, Math.PI * 2]);
        scene.Canvas.DrawRectangle(conic, 10, 10, 80, 80);

        var clip = Assert.Single(scene.Defs());
        Assert.Equal(Ns + "clipPath", clip.Name);
        Assert.Equal(Ns + "rect", Assert.Single(clip.Elements()).Name);

        var group = Assert.Single(scene.Drawn());
        Assert.Equal($"url(#{Attr(clip, "id")})", Attr(group, "clip-path"));
        var wedges = group.Elements().ToList();
        Assert.Equal(360, wedges.Count);
        Assert.All(wedges, w => Assert.StartsWith("M50 50L", Attr(w, "d")));

        // Clockwise from 12 o'clock: the start is red, a third of a turn on it is green, two thirds is blue.
        static (int R, int G, int B) Rgb(XElement wedge) => (Convert.ToInt32(Attr(wedge, "fill")[1..3], 16), Convert.ToInt32(Attr(wedge, "fill")[3..5], 16), Convert.ToInt32(Attr(wedge, "fill")[5..7], 16));
        Assert.True(Rgb(wedges[0]).R > 250);
        Assert.True(Rgb(wedges[120]).G > 250);
        Assert.True(Rgb(wedges[240]).B > 250);
    }

    [Fact]
    public void ACssConicGradientIsAForeignObjectInASwitchWithTheWedgesAsItsFallback()
    {
        using var scene = Create(options: new SvgComposerOptions { ConicGradients = SvgConicGradientMode.Css });
        var conic = scene.Context.GetConicGradientBrush(new PaintPoint(50, 50), 40,
            [C(255, 0, 0), C(0, 0, 255, 128)], [Math.PI / 2, Math.PI * 2.5]);
        scene.Canvas.DrawRectangle(conic, 10, 10, 80, 80);

        var clipped = Assert.Single(scene.Drawn());
        var @switch = Assert.Single(clipped.Elements());
        Assert.Equal(Ns + "switch", @switch.Name);

        var foreign = @switch.Elements().First();
        Assert.Equal(Ns + "foreignObject", foreign.Name);
        Assert.Equal("http://www.w3.org/1999/xhtml", Attr(foreign, "requiredExtensions"));
        Assert.Equal(("10", "10", "80", "80"), (Attr(foreign, "x"), Attr(foreign, "y"), Attr(foreign, "width"), Attr(foreign, "height")));
        var div = Assert.Single(foreign.Elements());
        // Stops are relative to the gradient's own start angle, as CSS counts them.
        Assert.Equal("width:100%;height:100%;background:conic-gradient(from 90deg at 50% 50%, rgba(255,0,0,1) 0deg, rgba(0,0,255,0.502) 360deg)", Attr(div, "style"));

        var fallback = @switch.Elements().Last();
        Assert.Equal(Ns + "g", fallback.Name);
        Assert.Equal(360, fallback.Elements().Count());
    }

    [Fact]
    public void ACssConicStrokeIsTheSameSwitchInsideTheMaskedGroup()
    {
        using var scene = Create(options: new SvgComposerOptions { ConicGradients = SvgConicGradientMode.Css });
        var pen = scene.Context.GetPen(scene.Context.GetConicGradientBrush(new PaintPoint(20, 20), 15, [C(255, 0, 0), C(0, 0, 255)], [0, Math.PI * 2]));
        pen.Width = 4;
        scene.Canvas.DrawRectangle(pen, 5, 5, 30, 30);

        var group = Assert.Single(scene.Drawn());
        Assert.StartsWith("url(#", Attr(group, "mask"));
        Assert.Equal(Ns + "switch", Assert.Single(group.Elements()).Name);
    }

    [Fact]
    public void ADefaultCanvasWritesNoForeignObject()
    {
        using var scene = Create();
        scene.Canvas.DrawRectangle(scene.Context.GetConicGradientBrush(new PaintPoint(5, 5), 5, [C(255, 0, 0), C(0, 0, 255)], [0, Math.PI]), 0, 0, 10, 10);

        Assert.DoesNotContain("foreignObject", scene.Page.ToSvg());
    }

    [Fact]
    public void AHatchBrushIsAPatternWhoseCellIsTheHatchSpacing()
    {
        using var scene = Create();
        scene.Canvas.DrawRectangle(new HatchBrush(HatchStyle.Cross, C(0, 0, 0), C(255, 255, 255), 8, 1), 0, 0, 80, 80);

        var pattern = scene.Defs().Single(e => e.Name == Ns + "pattern");
        Assert.Equal(("8", "8", "userSpaceOnUse"), (Attr(pattern, "width"), Attr(pattern, "height"), Attr(pattern, "patternUnits")));
        Assert.Equal(Attr(pattern, "id"), UrlId(Assert.Single(scene.Drawn())));
    }

    [Fact]
    public void ATileBrushIsAPatternOfTheTilesVectorContentCarriedByTheBrushTransform()
    {
        using var scene = Create();
        var (tileCanvas, tileImage) = scene.Canvas.CreateTile(10, 10)!.Value;
        tileCanvas.DrawRectangle(scene.Solid(255, 0, 0), 0, 0, 5, 5);
        tileCanvas.Dispose();

        scene.Canvas.DrawRectangle(new TileBrush(tileImage, 20, 30, Matrix3x2.CreateRotation(0.5f)), 0, 0, 100, 100);

        var defs = scene.Defs().ToList();
        var tile = defs.Single(e => e.Name == Ns + "g");
        Assert.Equal(Ns + "rect", Assert.Single(tile.Elements()).Name); // the tile's content, as vector
        Assert.Contains(defs, e => e.Name == Ns + "clipPath");

        var pattern = defs.Single(e => e.Name == Ns + "pattern");
        Assert.Equal(("20", "30"), (Attr(pattern, "width"), Attr(pattern, "height")));
        Assert.StartsWith("matrix(0.877", Attr(pattern, "patternTransform"));

        var use = Assert.Single(pattern.Elements());
        Assert.Equal("#" + Attr(tile, "id"), Attr(use, "href"));
        Assert.Equal("scale(2 3)", Attr(use, "transform")); // the 10x10 tile stretched to the 20x30 cell
    }

    [Fact]
    public void ATileIsDefinedOnceHoweverOftenItIsDrawn()
    {
        using var scene = Create();
        var (tileCanvas, tileImage) = scene.Canvas.CreateTile(10, 10)!.Value;
        tileCanvas.DrawRectangle(scene.Solid(0, 0, 0), 0, 0, 5, 5);
        tileCanvas.Dispose();

        scene.Canvas.DrawImage(tileImage, new Rect(0, 0, 10, 10));
        scene.Canvas.DrawImage(tileImage, new Rect(20, 0, 20, 20));

        Assert.Single(scene.Defs(), e => e.Name == Ns + "g");
        var uses = scene.Drawn().ToList();
        Assert.Equal(2, uses.Count);
        Assert.Equal("matrix(2 0 0 2 20 0)", Attr(uses[1], "transform"));
    }

    [Fact]
    public void ATileCanvasReportsItselfOffscreenAndSeedsTheCurrentTransform()
    {
        using var scene = Create();
        scene.Canvas.PushTransform(Matrix3x2.CreateScale(2, 2));
        var (tile, _) = scene.Canvas.CreateTile(10, 10)!.Value;

        Assert.True(tile.IsOffscreenTile);
        Assert.False(scene.Canvas.IsOffscreenTile);
        Assert.Equal(2, tile.TransformScale.X, 4);
        Assert.Same(scene.Canvas.TileCacheOwner, tile.TileCacheOwner);
        Assert.Null(scene.Canvas.CreateTile(0, 5));
    }
}
