using System;

namespace PeachDrawing;

/// <summary>
/// A set of closed polygonal contours in device space, the input the scanline rasterizer fills.
/// Contours are stored flat (interleaved x/y) with a start-index table so a large glyph run does not
/// allocate one array per contour.
/// </summary>
internal sealed class PolygonSet
{
    // Plain growable arrays with a count, not List<T>: a path is built point by point, and the list's version check and bounds work on every Add
    // is a measurable share of flattening a page of glyphs. Growth doubles, like the list's.
    private double[] _points = new double[32];
    private int _pointLength;
    private int[] _starts = new int[4];
    private int _startCount;

    public int ContourCount => _startCount;

    public int PointCount => _pointLength / 2;

    public void BeginContour()
    {
        if (_startCount == _starts.Length)
            Array.Resize(ref _starts, _starts.Length * 2);
        _starts[_startCount++] = _pointLength / 2;
    }

    public void Add(double x, double y)
    {
        if (_startCount == 0)
            BeginContour();

        if (_pointLength + 2 > _points.Length)
            Array.Resize(ref _points, _points.Length * 2);
        _points[_pointLength] = x;
        _points[_pointLength + 1] = y;
        _pointLength += 2;
    }

    /// <summary>The start (in points) and end (exclusive) of contour <paramref name="index"/>.</summary>
    public (int Start, int End) GetContour(int index)
    {
        var start = _starts[index];
        var end = index + 1 < _startCount ? _starts[index + 1] : _pointLength / 2;
        return (start, end);
    }

    public (double X, double Y) GetPoint(int pointIndex) => (_points[pointIndex * 2], _points[pointIndex * 2 + 1]);

    /// <summary>The tight bounding box of every point, or null when the set is empty.</summary>
    public (double MinX, double MinY, double MaxX, double MaxY)? GetBounds()
    {
        if (_pointLength == 0)
            return null;

        double minX = double.PositiveInfinity, minY = double.PositiveInfinity;
        double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity;
        var points = _points.AsSpan(0, _pointLength);
        for (var i = 0; i < points.Length; i += 2)
        {
            var x = points[i];
            var y = points[i + 1];
            if (x < minX) minX = x;
            if (x > maxX) maxX = x;
            if (y < minY) minY = y;
            if (y > maxY) maxY = y;
        }

        return (minX, minY, maxX, maxY);
    }

    /// <summary>Appends an axis-aligned rectangle as one contour.</summary>
    public void AddRectangle(double left, double top, double right, double bottom)
    {
        BeginContour();
        Add(left, top);
        Add(right, top);
        Add(right, bottom);
        Add(left, bottom);
    }

    /// <summary>Copies <paramref name="other"/>'s contours into this set, transformed by <paramref name="transform"/>.</summary>
    public void AddTransformed(PolygonSet other, in Affine transform)
    {
        for (var c = 0; c < other.ContourCount; c++)
        {
            var (start, end) = other.GetContour(c);
            BeginContour();
            for (var i = start; i < end; i++)
            {
                var (x, y) = other.GetPoint(i);
                var (tx, ty) = transform.Apply(x, y);
                Add(tx, ty);
            }
        }
    }

    public void Clear()
    {
        _pointLength = 0;
        _startCount = 0;
    }

    /// <summary>
    /// The signed area of contour <paramref name="index"/> (positive for a clockwise contour in a
    /// y-down device space). Used by the stroker to normalise the winding of every piece it emits.
    /// </summary>
    public double SignedArea(int index)
    {
        var (start, end) = GetContour(index);
        double sum = 0;
        for (var i = start; i < end; i++)
        {
            var j = i + 1 < end ? i + 1 : start;
            var (x0, y0) = GetPoint(i);
            var (x1, y1) = GetPoint(j);
            sum += x0 * y1 - x1 * y0;
        }

        return sum / 2;
    }

    /// <summary>Reverses the point order of contour <paramref name="index"/> in place.</summary>
    public void ReverseContour(int index)
    {
        var (start, end) = GetContour(index);
        for (int i = start, j = end - 1; i < j; i++, j--)
        {
            (_points[i * 2], _points[j * 2]) = (_points[j * 2], _points[i * 2]);
            (_points[i * 2 + 1], _points[j * 2 + 1]) = (_points[j * 2 + 1], _points[i * 2 + 1]);
        }
    }

    /// <summary>Makes every contour wind the same way (positive signed area), so a nonzero fill of the set is a union.</summary>
    public void NormalizeWinding()
    {
        for (var c = 0; c < ContourCount; c++)
        {
            if (SignedArea(c) < 0)
                ReverseContour(c);
        }
    }
}
