using PeachDrawing.Core;
using PeachDrawing.Core.Geometry;

namespace PeachDrawing.Tests;

public class PathStrokerTests
{
    private static GraphicsPath NewPath()
    {
        var ctx = new RasterRenderContext();
        return ctx.CreateCanvas(10, 10).GetGraphicsPath();
    }

    private static Pen NewPen(double width, LineCap cap = LineCap.Butt, LineJoin join = LineJoin.Miter)
    {
        var ctx = new RasterRenderContext();
        var pen = ctx.GetPen(PaintColor.Black);
        pen.Width = width;
        pen.LineCap = cap;
        pen.LineJoin = join;
        return pen;
    }

    [Fact]
    public void Stroke_OfAHorizontalLine_IsARectangleOfThePenWidth()
    {
        using var line = NewPath();
        line.Start(0, 10);
        line.LineTo(100, 10);
        using var outline = NewPath();

        PathStroker.Stroke(line, NewPen(4), outline);

        var m = new PathMeasure(outline);
        Assert.Equal(400, m.Area, 6);
        Assert.Equal(new Rect(0, 8, 100, 4), m.Bounds);
        Assert.Equal(FillMode.Nonzero, outline.FillMode);
    }

    [Fact]
    public void Stroke_WithSquareCaps_ExtendsBothEndsByHalfTheWidth()
    {
        using var line = NewPath();
        line.Start(0, 10);
        line.LineTo(100, 10);
        using var outline = NewPath();

        PathStroker.Stroke(line, NewPen(4, LineCap.Square), outline);

        Assert.Equal(new Rect(-2, 8, 104, 4), new PathMeasure(outline).Bounds);
    }

    [Fact]
    public void Stroke_WithRoundCaps_AddsRoughlyACircleOfArea()
    {
        using var line = NewPath();
        line.Start(0, 10);
        line.LineTo(100, 10);
        using var outline = NewPath();

        PathStroker.Stroke(line, NewPen(10, LineCap.Round), outline);

        Assert.Equal(1000 + Math.PI * 25, new PathMeasure(outline).Area, 3.0);
    }

    [Fact]
    public void Stroke_OfAClosedSquare_WithMiterJoins_HasSharpOuterCorners()
    {
        using var square = NewPath();
        square.AddPolygon([new PaintPoint(10, 10), new PaintPoint(50, 10), new PaintPoint(50, 50), new PaintPoint(10, 50)]);
        using var outline = NewPath();

        PathStroker.Stroke(square, NewPen(4), outline);

        Assert.Equal(new Rect(8, 8, 44, 44), new PathMeasure(outline).Bounds);
    }

    [Fact]
    public void Stroke_WithADashPattern_CoversLessThanASolidLine()
    {
        using var line = NewPath();
        line.Start(0, 10);
        line.LineTo(100, 10);
        var pen = NewPen(2);
        pen.SetDashPattern([4, 4], 0);
        using var dashed = NewPath();
        using var solid = NewPath();

        PathStroker.Stroke(line, pen, dashed);
        PathStroker.Stroke(line, NewPen(2), solid);

        Assert.True(new PathMeasure(dashed).Area < new PathMeasure(solid).Area * 0.75);
    }

    [Fact]
    public void Stroke_WithAZeroWidthPen_AddsNothing()
    {
        using var line = NewPath();
        line.Start(0, 0);
        line.LineTo(10, 0);
        using var outline = NewPath();

        PathStroker.Stroke(line, NewPen(0), outline);

        Assert.Empty(outline.GetCurveContours());
    }

    [Fact]
    public void Stroke_OfACurve_FollowsIt()
    {
        using var curve = NewPath();
        curve.Start(0, 0);
        curve.AddBezierTo(0, 40, 60, 40, 60, 0);
        using var outline = NewPath();

        PathStroker.Stroke(curve, NewPen(2), outline);

        var bounds = new PathMeasure(outline).Bounds;
        Assert.True(bounds.Bottom > 29 && bounds.Bottom < 32);
    }

    [Fact]
    public void Stroke_ValidatesItsArguments()
    {
        using var path = NewPath();
        using var outline = NewPath();
        var pen = NewPen(1);

        Assert.Throws<ArgumentNullException>(() => PathStroker.Stroke(null!, pen, outline));
        Assert.Throws<ArgumentNullException>(() => PathStroker.Stroke(path, null!, outline));
        Assert.Throws<ArgumentNullException>(() => PathStroker.Stroke(path, pen, null!));
        PathStroker.Stroke(path, pen, outline, tolerance: -1);
    }
}
