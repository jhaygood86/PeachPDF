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

    [Fact]
    public void Stroke_OfAClosedLineOutAndBack_HasJoinsAtBothEndsAndNoCaps()
    {
        using var line = NewPath();
        line.Start(0, 10);
        line.LineTo(100, 10);
        line.CloseFigure();

        // A closed subpath of two points is a line there and back: its ends are 180 degree turns, which a miter cannot make, so a square cap is not drawn and nothing sticks out.
        using var mitered = NewPath();
        PathStroker.Stroke(line, NewPen(4, LineCap.Square), mitered);
        Assert.Equal(new Rect(0, 8, 100, 4), new PathMeasure(mitered).Bounds);

        // A round join turns it with the half disc the path was heading into, at each end.
        using var rounded = NewPath();
        PathStroker.Stroke(line, NewPen(4, LineCap.Butt, LineJoin.Round), rounded);
        var bounds = new PathMeasure(rounded).Bounds;
        Assert.InRange(bounds.Left, -2.01, -1.8);
        Assert.InRange(bounds.Right, 101.8, 102.01);
    }

    [Fact]
    public void Stroke_WithAMiterLimitBelowOne_BevelsEveryJoin()
    {
        var context = new RasterRenderContext();
        using var canvas = context.CreateCanvas(20, 20);
        using var square = canvas.GetGraphicsPath();
        square.Start(5, 5);
        square.LineTo(15, 5);
        square.LineTo(15, 15);
        square.LineTo(5, 15);
        square.CloseFigure();

        byte CornerAlpha(double miterLimit)
        {
            var pen = NewPen(2);
            pen.MiterLimit = miterLimit;
            using var outline = canvas.GetGraphicsPath();
            PathStroker.Stroke(square, pen, outline);
            using var sheet = context.CreateCanvas(20, 20);
            using var drawn = sheet.GetGraphicsPath();
            drawn.AddPath(outline);
            sheet.DrawPath(context.GetSolidBrush(PaintColor.Black), drawn);
            return sheet.ToPixelBuffer().PremultipliedRgba.ToArray()[(4 * 20 + 4) * 4 + 3];
        }

        // The outer corner pixel is wholly inside a miter and half outside a bevel.
        Assert.Equal(255, CornerAlpha(10));
        Assert.InRange(CornerAlpha(0.5), 100, 160);
    }

    [Fact]
    public void Stroke_OfAThickArcAroundItsCenter_DoesNotSpreadOutsideTheArcsWedge()
    {
        // A quarter circle of radius 25 stroked 200 wide: every point of it is the middle of a line 200 long through the circle's center, so the stroke is two wedges from the
        // center and nothing beside them. Cut coarsely, the chords' rectangles are wider than the distance from the center and cover it.
        var context = new RasterRenderContext();
        using var canvas = context.CreateCanvas(200, 100);
        using var arc = canvas.GetGraphicsPath();
        arc.Start(125, 50);
        arc.AddBezierTo(125, 50 - 13.81, 100 + 13.81, 25, 100, 25);
        using var outline = canvas.GetGraphicsPath();

        PathStroker.Stroke(arc, NewPen(200), outline);
        canvas.DrawPath(context.GetSolidBrush(PaintColor.Black), outline);

        var pixels = canvas.ToPixelBuffer().PremultipliedRgba.ToArray();
        byte Alpha(int x, int y) => pixels[(y * 200 + x) * 4 + 3];
        Assert.Equal(255, Alpha(110, 40));
        Assert.Equal(255, Alpha(90, 60));
        Assert.Equal(0, Alpha(101, 51));
        Assert.Equal(0, Alpha(110, 60));
        Assert.Equal(0, Alpha(90, 40));
    }
}
