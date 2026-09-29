using System.Numerics;
using PeachDrawing.Core;

namespace PeachDrawing.Tests;

public class TileBrushTests
{
    private static RasterCanvas NewCanvas(int size = 40)
    {
        var canvas = new RasterRenderContext().CreateCanvas(size, size);
        canvas.DrawRectangle(canvas.GetSolidBrush(PaintColor.White), 0, 0, size, size);
        return canvas;
    }

    /// <summary>A tile whose left half is red and right half blue, 10 units wide and 10 high.</summary>
    private static Image RedBlueTile(RasterCanvas canvas)
    {
        var (g, image) = canvas.CreateTile(10, 10)!.Value;
        g.DrawRectangle(g.GetSolidBrush(PaintColor.FromArgb(255, 255, 0, 0)), 0, 0, 5, 10);
        g.DrawRectangle(g.GetSolidBrush(PaintColor.FromArgb(255, 0, 0, 255)), 5, 0, 5, 10);
        g.Dispose();
        return image;
    }

    private static (byte R, byte G, byte B, byte A) At(RasterCanvas canvas, int x, int y)
    {
        var buffer = canvas.ToPixelBuffer();
        var span = buffer.PremultipliedRgba.Span;
        var i = (y * buffer.Width + x) * 4;
        return (span[i], span[i + 1], span[i + 2], span[i + 3]);
    }

    [Fact]
    public void TileBrush_RepeatsTheTileAcrossTheShape()
    {
        using var canvas = NewCanvas();
        var brush = new TileBrush(RedBlueTile(canvas), 10, 10);

        canvas.DrawRectangle(brush, 0, 0, 40, 40);

        // Every cell is red on its left half and blue on its right half.
        foreach (var cell in new[] { 0, 10, 20, 30 })
        {
            Assert.Equal(255, At(canvas, cell + 2, 5).R);
            Assert.Equal(255, At(canvas, cell + 7, 5).B);
            Assert.Equal(0, At(canvas, cell + 7, 5).R);
        }
    }

    [Fact]
    public void TileBrush_TransformMovesTheGrid()
    {
        using var canvas = NewCanvas();
        var moved = new TileBrush(RedBlueTile(canvas), 10, 10, Matrix3x2.CreateTranslation(5, 0));

        canvas.DrawRectangle(moved, 0, 0, 40, 40);

        // With the grid shifted 5 right, the red half now starts at x = 5.
        Assert.Equal(255, At(canvas, 7, 5).R);
        Assert.Equal(255, At(canvas, 2, 5).B);
    }

    [Fact]
    public void TileBrush_TransformScaleStretchesTheCells()
    {
        using var canvas = NewCanvas(60);
        var scaled = new TileBrush(RedBlueTile(canvas), 10, 10, Matrix3x2.CreateScale(2, 1));

        canvas.DrawRectangle(scaled, 0, 0, 60, 30);

        // Cells are now 20 wide: red for 0-10, blue for 10-20, then red again.
        Assert.Equal(255, At(canvas, 4, 5).R);
        Assert.Equal(255, At(canvas, 14, 5).B);
        Assert.Equal(255, At(canvas, 24, 5).R);
    }

    [Fact]
    public void TileBrush_ContinuesAcrossSeparateShapes()
    {
        using var canvas = NewCanvas();
        var brush = new TileBrush(RedBlueTile(canvas), 10, 10);

        canvas.DrawRectangle(brush, 0, 0, 13, 40);
        canvas.DrawRectangle(brush, 13, 0, 27, 40);

        // The seam at x=13 falls inside a cell: pixels 12 and 13 are both in the red half of the cell that starts at 10... 13 is red, 15 blue.
        Assert.Equal(255, At(canvas, 12, 5).R);
        Assert.Equal(255, At(canvas, 13, 5).R);
        Assert.Equal(255, At(canvas, 16, 5).B);
    }

    [Fact]
    public void Pen_CanStrokeWithATile()
    {
        using var canvas = NewCanvas();
        var pen = canvas.GetPen(new TileBrush(RedBlueTile(canvas), 10, 10));
        pen.Width = 10;

        canvas.DrawLine(pen, 0, 20, 40, 20);

        Assert.Equal(255, At(canvas, 2, 20).R);
        Assert.Equal(255, At(canvas, 7, 20).B);
        Assert.Equal(255, At(canvas, 2, 2).R);
        Assert.Equal(255, At(canvas, 2, 2).G);
    }

    [Fact]
    public void HatchBrush_Horizontal_DrawsLinesOverTheBackground()
    {
        using var canvas = NewCanvas();
        var hatch = new HatchBrush(HatchStyle.Horizontal, PaintColor.Black, PaintColor.White, spacing: 10, lineWidth: 2);

        canvas.DrawRectangle(hatch, 0, 0, 40, 40);

        // The line is centred in each 10-unit cell (rows 4 and 5); between lines the background shows.
        foreach (var cell in new[] { 0, 10, 20, 30 })
        {
            Assert.True(At(canvas, 20, cell + 4).R < 40);
            Assert.True(At(canvas, 20, cell + 5).R < 40);
            Assert.Equal(255, At(canvas, 20, cell + 1).R);
        }
    }

    [Fact]
    public void HatchBrush_Cross_DrawsBothDirections()
    {
        using var canvas = NewCanvas();
        canvas.DrawRectangle(new HatchBrush(HatchStyle.Cross, PaintColor.Black, PaintColor.White, 10, 2), 0, 0, 40, 40);

        Assert.True(At(canvas, 20, 14).R < 40);
        Assert.True(At(canvas, 14, 20).R < 40);
        Assert.Equal(255, At(canvas, 11, 11).R);
    }

    [Theory]
    [InlineData(HatchStyle.ForwardDiagonal)]
    [InlineData(HatchStyle.BackwardDiagonal)]
    [InlineData(HatchStyle.DiagonalCross)]
    public void HatchBrush_Diagonals_DrawInkAndLeaveBackground(HatchStyle style)
    {
        using var canvas = NewCanvas();
        canvas.DrawRectangle(new HatchBrush(style, PaintColor.Black, PaintColor.White, 10, 2), 0, 0, 40, 40);

        var dark = 0;
        var light = 0;
        for (var y = 0; y < 40; y++)
        {
            for (var x = 0; x < 40; x++)
            {
                if (At(canvas, x, y).R < 100) dark++;
                else if (At(canvas, x, y).R > 200) light++;
            }
        }

        Assert.True(dark > 100);
        Assert.True(light > 600);
    }

    [Fact]
    public void ForwardDiagonal_RunsTopLeftToBottomRight()
    {
        using var canvas = NewCanvas();
        canvas.DrawRectangle(new HatchBrush(HatchStyle.ForwardDiagonal, PaintColor.Black, PaintColor.White, 10, 2), 0, 0, 40, 40);

        Assert.True(At(canvas, 15, 15).R < 100);
        Assert.True(At(canvas, 25, 25).R < 100);
        Assert.Equal(255, At(canvas, 25, 20).R);
    }

    [Fact]
    public void HatchBrush_WithATransparentBackground_LeavesThePageShowing()
    {
        using var canvas = NewCanvas();
        canvas.DrawRectangle(canvas.GetSolidBrush(PaintColor.FromArgb(255, 0, 255, 0)), 0, 0, 40, 40);

        canvas.DrawRectangle(new HatchBrush(HatchStyle.Horizontal, PaintColor.Black, PaintColor.FromArgb(0, 0, 0, 0), 10, 2), 0, 0, 40, 40);

        Assert.Equal(255, At(canvas, 20, 1).G);
        Assert.True(At(canvas, 20, 4).G < 60);
    }

    [Fact]
    public void Constructors_ValidateTheirArguments()
    {
        using var canvas = NewCanvas();
        var tile = RedBlueTile(canvas);

        Assert.Throws<ArgumentNullException>(() => new TileBrush(null!, 1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TileBrush(tile, 0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TileBrush(tile, 1, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TileBrush(tile, double.PositiveInfinity, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new HatchBrush(HatchStyle.Cross, PaintColor.Black, PaintColor.White, 0));
        Assert.Throws<ArgumentNullException>(() => new HatchBrush(HatchStyle.Cross, PaintColor.Black, PaintColor.White).ToTileBrush(null!));
    }

    [Fact]
    public void HatchBrush_DefaultsLineWidthToATenthOfTheSpacing()
    {
        Assert.Equal(1.6, new HatchBrush(HatchStyle.Cross, PaintColor.Black, PaintColor.White, 16).LineWidth, 9);
        Assert.Equal(3, new HatchBrush(HatchStyle.Cross, PaintColor.Black, PaintColor.White, 16, 3).LineWidth, 9);
    }
}
