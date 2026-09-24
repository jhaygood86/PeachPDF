using System;
using System.Buffers;
using System.Collections.Generic;

namespace PeachPDF.Raster;

/// <summary>Receives the anti-aliased coverage of one scanline run produced by <see cref="ScanlineRasterizer"/>.</summary>
internal interface ICoverageSink
{
    /// <summary>
    /// <paramref name="coverage"/> holds one 0-255 coverage byte per pixel starting at column
    /// <paramref name="x0"/> of row <paramref name="y"/>. Called at most once per row, in increasing y.
    /// </summary>
    void Span(int y, int x0, ReadOnlySpan<byte> coverage);
}

/// <summary>
/// An anti-aliased scanline polygon rasterizer with exact horizontal span coverage and
/// <see cref="SubScanlines"/> vertical samples per pixel row, evaluated per sub-scanline against either
/// fill rule. Coverage is accumulated in integers, so output is deterministic on every CPU.
/// </summary>
/// <remarks>
/// Chosen over a signed-area accumulation rasterizer because that approach cannot evaluate the even-odd
/// rule (or nonzero on overlapping same-direction contours) correctly, and the stroker relies on filling a
/// union of overlapping pieces nonzero.
/// </remarks>
internal static class ScanlineRasterizer
{
    /// <summary>Vertical samples per pixel row. Horizontal coverage is exact to 1/256 of a pixel per sample.</summary>
    public const int SubScanlines = 16;

    private const int Fixed = 256;
    private const int TotalPerPixel = SubScanlines * Fixed;

    private struct Edge
    {
        public double YTop;
        public double YBottom;
        public double X0;
        public double DxDy;
        public int Direction;
    }

    public static void Fill<TSink>(PolygonSet polygons, bool evenOdd, IntRect clip, ref TSink sink)
        where TSink : struct, ICoverageSink
    {
        if (clip.IsEmpty || polygons.PointCount < 3)
            return;

        var bounds = polygons.GetBounds();
        if (bounds is not { } b)
            return;

        var minY = Math.Max(clip.Top, (int)Math.Floor(Math.Max(b.MinY, -1e9)));
        var maxY = Math.Min(clip.Bottom, (int)Math.Ceiling(Math.Min(b.MaxY, 1e9)));
        if (maxY <= minY)
            return;

        var edges = BuildEdges(polygons, minY, maxY);
        if (edges.Length == 0)
            return;

        Array.Sort(edges, static (a, c) => a.YTop.CompareTo(c.YTop));

        var width = clip.Width;
        var cover = ArrayPool<int>.Shared.Rent(width + 2);
        var delta = ArrayPool<int>.Shared.Rent(width + 2);
        var alpha = ArrayPool<byte>.Shared.Rent(width + 2);
        var active = new int[Math.Min(edges.Length, 1024)];
        var activeCount = 0;
        var xs = new double[active.Length];
        var dirs = new int[active.Length];
        Array.Clear(cover, 0, width + 2);
        Array.Clear(delta, 0, width + 2);

        try
        {
            var nextEdge = 0;

            for (var y = minY; y < maxY; y++)
            {
                var rowMin = int.MaxValue;
                var rowMax = int.MinValue;

                for (var k = 0; k < SubScanlines; k++)
                {
                    var sy = y + (k + 0.5) / SubScanlines;

                    while (nextEdge < edges.Length && edges[nextEdge].YTop <= sy)
                    {
                        if (activeCount == active.Length)
                        {
                            Array.Resize(ref active, active.Length * 2);
                            Array.Resize(ref xs, active.Length);
                            Array.Resize(ref dirs, active.Length);
                        }

                        active[activeCount++] = nextEdge++;
                    }

                    // Drop finished edges, compute this sample's crossings, keep them sorted by x.
                    var crossings = 0;
                    var write = 0;
                    for (var i = 0; i < activeCount; i++)
                    {
                        ref readonly var e = ref edges[active[i]];
                        if (e.YBottom <= sy)
                            continue;

                        active[write++] = active[i];

                        var x = e.X0 + (sy - e.YTop) * e.DxDy;
                        var d = e.Direction;
                        var pos = crossings++;
                        while (pos > 0 && xs[pos - 1] > x)
                        {
                            xs[pos] = xs[pos - 1];
                            dirs[pos] = dirs[pos - 1];
                            pos--;
                        }

                        xs[pos] = x;
                        dirs[pos] = d;
                    }

                    activeCount = write;
                    if (crossings < 2)
                        continue;

                    var winding = 0;
                    var inside = false;
                    double spanStart = 0;
                    for (var i = 0; i < crossings; i++)
                    {
                        bool wasInside = inside;
                        if (evenOdd)
                        {
                            inside = !inside;
                        }
                        else
                        {
                            winding += dirs[i];
                            inside = winding != 0;
                        }

                        if (!wasInside && inside)
                        {
                            spanStart = xs[i];
                        }
                        else if (wasInside && !inside)
                        {
                            Accumulate(spanStart, xs[i], clip.Left, clip.Right, cover, delta, ref rowMin, ref rowMax);
                        }
                    }
                }

                if (rowMin <= rowMax)
                {
                    var first = rowMin - clip.Left;
                    var last = rowMax - clip.Left;
                    var count = last - first + 1;
                    var running = 0;

                    // delta carries full-pixel runs, always starting after the row's first touched column,
                    // so nothing before `first` needs summing.
                    for (var i = 0; i < count; i++)
                    {
                        var col = first + i;
                        running += delta[col];
                        var total = cover[col] + running;
                        if (total > TotalPerPixel) total = TotalPerPixel;
                        else if (total < 0) total = 0;
                        alpha[i] = (byte)((total * 255 + TotalPerPixel / 2) / TotalPerPixel);
                        cover[col] = 0;
                        delta[col] = 0;
                    }

                    // The run-end delta can sit one column past the last covered pixel.
                    delta[last + 1] = 0;
                    cover[last + 1] = 0;

                    sink.Span(y, rowMin, new ReadOnlySpan<byte>(alpha, 0, count));
                }
            }
        }
        finally
        {
            ArrayPool<int>.Shared.Return(cover);
            ArrayPool<int>.Shared.Return(delta);
            ArrayPool<byte>.Shared.Return(alpha);
        }
    }

    private static void Accumulate(double xa, double xb, int left, int right, int[] cover, int[] delta, ref int rowMin, ref int rowMax)
    {
        if (xa < left) xa = left;
        if (xb > right) xb = right;
        if (xb <= xa)
            return;

        var ia = (int)Math.Floor(xa);
        var ib = (int)Math.Floor(xb);
        if (ib >= right)
        {
            // xb sits exactly on the right clip edge: the last covered pixel is right - 1, full.
            ib = right;
        }

        var ca = ia - left;
        if (ia == ib)
        {
            cover[ca] += (int)Math.Round((xb - xa) * Fixed);
            Touch(ia, ia, ref rowMin, ref rowMax);
            return;
        }

        cover[ca] += (int)Math.Round((ia + 1 - xa) * Fixed);
        var fullFrom = ia + 1;
        var lastPixel = ia;
        if (ib > fullFrom)
        {
            delta[fullFrom - left] += Fixed;
            delta[ib - left] -= Fixed;
            lastPixel = ib - 1;
        }

        if (ib < right)
        {
            var tail = (int)Math.Round((xb - ib) * Fixed);
            if (tail > 0)
            {
                cover[ib - left] += tail;
                lastPixel = ib;
            }
        }

        Touch(ia, lastPixel, ref rowMin, ref rowMax);
    }

    private static void Touch(int first, int last, ref int rowMin, ref int rowMax)
    {
        if (first < rowMin) rowMin = first;
        if (last > rowMax) rowMax = last;
    }

    private static Edge[] BuildEdges(PolygonSet polygons, int minY, int maxY)
    {
        var list = new List<Edge>(polygons.PointCount);
        for (var c = 0; c < polygons.ContourCount; c++)
        {
            var (start, end) = polygons.GetContour(c);
            if (end - start < 2)
                continue;

            for (var i = start; i < end; i++)
            {
                var j = i + 1 < end ? i + 1 : start;
                var (x0, y0) = polygons.GetPoint(i);
                var (x1, y1) = polygons.GetPoint(j);
                if (y0 == y1 || double.IsNaN(x0 + y0 + x1 + y1))
                    continue;

                int dir;
                if (y0 < y1)
                {
                    dir = 1;
                }
                else
                {
                    dir = -1;
                    (x0, y0, x1, y1) = (x1, y1, x0, y0);
                }

                if (y1 <= minY || y0 >= maxY)
                    continue;

                list.Add(new Edge
                {
                    YTop = y0,
                    YBottom = y1,
                    X0 = x0,
                    DxDy = (x1 - x0) / (y1 - y0),
                    Direction = dir,
                });
            }
        }

        return list.ToArray();
    }
}
