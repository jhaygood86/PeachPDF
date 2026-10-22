using PeachDrawing.Core;
using PeachDrawing.Core.Geometry;
using static PeachDrawing.Svg.Tests.SvgScene;

namespace PeachDrawing.Svg.Tests;

public class SvgShapeTests
{
    [Fact]
    public void FilledRectangleIsARectElementWithItsGeometryAndColour()
    {
        using var scene = Create();
        scene.Canvas.DrawRectangle(scene.Solid(200, 30, 30), 10, 20, 30.5, 40);

        var rect = Assert.Single(scene.Drawn());
        Assert.Equal(Ns + "rect", rect.Name);
        Assert.Equal("10", Attr(rect, "x"));
        Assert.Equal("20", Attr(rect, "y"));
        Assert.Equal("30.5", Attr(rect, "width"));
        Assert.Equal("40", Attr(rect, "height"));
        Assert.Equal("#c81e1e", Attr(rect, "fill"));
        Assert.Equal(string.Empty, Attr(rect, "fill-opacity"));
    }

    [Fact]
    public void ATranslucentColourWritesItsAlphaAsFillOpacity()
    {
        using var scene = Create();
        scene.Canvas.DrawRectangle(scene.Solid(0, 0, 255, 128), 0, 0, 10, 10);

        Assert.Equal("0.502", Attr(Assert.Single(scene.Drawn()), "fill-opacity"));
    }

    [Fact]
    public void AFullyTransparentColourDrawsNothing()
    {
        using var scene = Create();
        scene.Canvas.DrawRectangle(scene.Solid(0, 0, 0, 0), 0, 0, 10, 10);

        Assert.Empty(scene.Drawn());
    }

    [Fact]
    public void ACmykColourIsWrittenAsItsRgbRendition()
    {
        using var scene = Create();
        scene.Canvas.DrawRectangle(scene.Context.GetSolidBrush(PaintColor.FromCmyk(255, 0f, 1f, 1f, 0f)), 0, 0, 10, 10);

        Assert.Equal("#ff0000", Attr(Assert.Single(scene.Drawn()), "fill"));
    }

    [Fact]
    public void APathKeepsItsCurvesAndClosesItsFigures()
    {
        using var scene = Create();
        var path = scene.Canvas.GetGraphicsPath();
        path.Start(0, 0);
        path.LineTo(10, 0);
        path.AddBezierTo(15, 0, 20, 5, 20, 10);
        path.CloseFigure();
        scene.Canvas.DrawPath(scene.Solid(0, 0, 0), path);

        var d = Attr(Assert.Single(scene.Drawn()), "d");
        Assert.Equal("M0 0L10 0C15 0 20 5 20 10Z", d);
    }

    [Fact]
    public void ACircleIsWrittenAsCubicCurvesNotAsFlattenedLines()
    {
        using var scene = Create();
        var path = scene.Canvas.GetGraphicsPath();
        path.AddCircle(50, 50, 20);
        scene.Canvas.DrawPath(scene.Solid(0, 0, 0), path);

        var d = Attr(Assert.Single(scene.Drawn()), "d");
        Assert.Equal(4, d.Count(c => c == 'C'));
        Assert.DoesNotContain('L', d);
    }

    [Fact]
    public void AnEvenOddPathSaysSo()
    {
        using var scene = Create();
        var path = scene.Canvas.GetGraphicsPath();
        path.FillMode = FillMode.EvenOdd;
        path.AddCircle(10, 10, 8);
        scene.Canvas.DrawPath(scene.Solid(0, 0, 0), path);

        Assert.Equal("evenodd", Attr(Assert.Single(scene.Drawn()), "fill-rule"));
    }

    [Fact]
    public void ASeveralSubpathPathKeepsEachSubpath()
    {
        using var scene = Create();
        var path = scene.Canvas.GetGraphicsPath();
        path.AddMove(0, 0);
        path.LineTo(5, 5);
        path.AddMove(20, 20);
        path.LineTo(30, 30);
        scene.Canvas.DrawPath(scene.Solid(0, 0, 0), path);

        Assert.Equal("M0 0L5 5M20 20L30 30", Attr(Assert.Single(scene.Drawn()), "d"));
    }

    [Fact]
    public void APolygonIsAPolygonElementAndAnOpenOneWithTwoPointsIsSkipped()
    {
        using var scene = Create();
        scene.Canvas.DrawPolygon(scene.Solid(0, 0, 0), [new PaintPoint(0, 0), new PaintPoint(10, 0), new PaintPoint(5, 8)]);
        scene.Canvas.DrawPolygon(scene.Solid(0, 0, 0), [new PaintPoint(0, 0), new PaintPoint(10, 0)]);

        var polygon = Assert.Single(scene.Drawn());
        Assert.Equal(Ns + "polygon", polygon.Name);
        Assert.Equal("0,0 10,0 5,8", Attr(polygon, "points"));
    }

    [Fact]
    public void AStrokedLineCarriesThePensWidthColourCapAndJoin()
    {
        using var scene = Create();
        var pen = scene.Pen(10, 20, 30, 3);
        pen.LineCap = LineCap.Round;
        pen.LineJoin = LineJoin.Bevel;
        pen.MiterLimit = 7;
        scene.Canvas.DrawLine(pen, 1, 2, 3, 4);

        var line = Assert.Single(scene.Drawn());
        Assert.Equal(Ns + "line", line.Name);
        Assert.Equal("none", Attr(line, "fill"));
        Assert.Equal("#0a141e", Attr(line, "stroke"));
        Assert.Equal("3", Attr(line, "stroke-width"));
        Assert.Equal("round", Attr(line, "stroke-linecap"));
        Assert.Equal("bevel", Attr(line, "stroke-linejoin"));
        Assert.Equal("7", Attr(line, "stroke-miterlimit"));
    }

    [Fact]
    public void ADefaultPenWritesNoCapOrJoinAndTheMiterLimitThePdfCanvasUses()
    {
        using var scene = Create();
        scene.Canvas.DrawRectangle(scene.Pen(0, 0, 0, 1), 0, 0, 10, 10);

        var rect = Assert.Single(scene.Drawn());
        Assert.Equal(string.Empty, Attr(rect, "stroke-linecap"));
        Assert.Equal(string.Empty, Attr(rect, "stroke-linejoin"));
        Assert.Equal("10", Attr(rect, "stroke-miterlimit")); // an unset limit is 10, as on the PDF and raster canvases
        Assert.Equal(string.Empty, Attr(rect, "stroke-dasharray"));
    }

    [Fact]
    public void AZeroWidthPenDrawsAHairlineThatDoesNotScale()
    {
        using var scene = Create();
        scene.Canvas.DrawLine(scene.Pen(0, 0, 0, 0), 0, 0, 10, 10);

        var line = Assert.Single(scene.Drawn());
        Assert.Equal("1", Attr(line, "stroke-width"));
        Assert.Equal("non-scaling-stroke", Attr(line, "vector-effect"));
    }

    [Theory]
    [InlineData(DashStyle.Dash, 4, "12 4")]
    [InlineData(DashStyle.Dash, 1, "4 4")]
    [InlineData(DashStyle.Dot, 4, "4")]
    [InlineData(DashStyle.DashDot, 2, "6 2 2 2")]
    [InlineData(DashStyle.DashDotDot, 2, "6 2 2 2 2 2")]
    public void DashPresetsAreMultiplesOfThePensWidth(DashStyle style, double width, string expected)
    {
        using var scene = Create();
        var pen = scene.Pen(0, 0, 0, width);
        pen.DashStyle = style;
        scene.Canvas.DrawLine(pen, 0, 0, 50, 0);

        Assert.Equal(expected, Attr(Assert.Single(scene.Drawn()), "stroke-dasharray"));
    }

    [Fact]
    public void ACustomDashPatternAndOffsetAreWrittenAndAnOddPatternIsPadded()
    {
        using var scene = Create();
        var pen = scene.Pen(0, 0, 0, 5);
        pen.SetDashPattern([6, 2, 3], 1.5);
        scene.Canvas.DrawLine(pen, 0, 0, 50, 0);

        var line = Assert.Single(scene.Drawn());
        Assert.Equal("6 2 3 1", Attr(line, "stroke-dasharray"));
        Assert.Equal("1.5", Attr(line, "stroke-dashoffset"));
    }

    [Fact]
    public void APenWithAGradientStrokesWithAGradientReference()
    {
        using var scene = Create();
        var pen = scene.Context.GetPen(scene.Context.GetLinearGradientBrush(new PaintPoint(0, 0), new PaintPoint(10, 0),
            [(PaintColor.Black, 0), (PaintColor.White, 1)]));
        pen.Width = 2;
        scene.Canvas.DrawLine(pen, 0, 0, 10, 0);

        var stroke = Attr(Assert.Single(scene.Drawn()), "stroke");
        Assert.StartsWith("url(#", stroke);
        Assert.Equal(Attr(Assert.Single(scene.Defs()), "id"), stroke[5..^1]);
    }

    [Fact]
    public void CoordinatesAreRoundedToTheConfiguredPrecision()
    {
        using var scene = Create(options: new SvgComposerOptions { Precision = 1 });
        scene.Canvas.DrawRectangle(scene.Solid(0, 0, 0), 1.26, 2.04, 3.333, 4.449);

        var rect = Assert.Single(scene.Drawn());
        Assert.Equal("1.3", Attr(rect, "x"));
        Assert.Equal("2", Attr(rect, "y"));
        Assert.Equal("3.3", Attr(rect, "width"));
        Assert.Equal("4.4", Attr(rect, "height"));
    }

    [Fact]
    public void PathCoordinatesAndPenWidthsAreInPointsWhileEverythingElseIsInLayoutUnits()
    {
        // At two layout units to the point, a path (and a pen's width and dashes) is scaled up to the document's units; a rectangle,
        // a line and a clip rectangle are already in them - the same reading the PDF and raster canvases give these calls.
        using var scene = Create(pixelsPerPoint: 2);
        var path = scene.Canvas.GetGraphicsPath();
        path.Start(1, 2);
        path.AddBezierTo(2, 2, 3, 3, 3, 4);
        var pen = scene.Pen(0, 0, 0, 1.5);
        pen.SetDashPattern([2, 1], 0.5);
        scene.Canvas.DrawPath(pen, path);
        scene.Canvas.DrawRectangle(scene.Solid(0, 0, 0), 1, 2, 3, 4);
        scene.Canvas.DrawLine(pen, 1, 1, 2, 2);
        scene.Canvas.PushClip(path);
        scene.Canvas.PopClip();

        var drawn = scene.Drawn().ToList();
        Assert.Equal("M2 4C4 4 6 6 6 8", Attr(drawn[0], "d"));
        Assert.Equal(("3", "4 2", "1"), (Attr(drawn[0], "stroke-width"), Attr(drawn[0], "stroke-dasharray"), Attr(drawn[0], "stroke-dashoffset")));
        Assert.Equal(("1", "2", "3", "4"), (Attr(drawn[1], "x"), Attr(drawn[1], "y"), Attr(drawn[1], "width"), Attr(drawn[1], "height")));
        Assert.Equal(("1", "2"), (Attr(drawn[2], "x1"), Attr(drawn[2], "x2")));
        Assert.Equal("M2 4C4 4 6 6 6 8", Attr(Assert.Single(Assert.Single(scene.Defs()).Elements()), "d"));
    }

    [Fact]
    public void AStrokeWithAConicGradientIsTheWedgesPaintedThroughAMaskOfTheStroke()
    {
        using var scene = Create();
        var conic = scene.Context.GetConicGradientBrush(new PaintPoint(5, 5), 5, [PaintColor.FromArgb(255, 255, 0, 0), PaintColor.FromArgb(255, 0, 0, 255)], [0, Math.PI]);
        var pen = scene.Context.GetPen(conic);
        pen.Width = 2;
        scene.Canvas.DrawRectangle(pen, 0, 0, 10, 10);

        var mask = Assert.Single(scene.Defs());
        Assert.Equal(Ns + "mask", mask.Name);
        var stroke = Assert.Single(mask.Elements());
        Assert.Equal(("#ffffff", "2", "none"), (Attr(stroke, "stroke"), Attr(stroke, "stroke-width"), Attr(stroke, "fill")));

        var group = Assert.Single(scene.Drawn());
        Assert.Equal($"url(#{Attr(mask, "id")})", Attr(group, "mask"));
        Assert.Equal(360, group.Elements().Count());
    }
}
