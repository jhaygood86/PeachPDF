using PeachDrawing.Core;
using System;
using System.Buffers;

namespace PeachDrawing;

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
/// <para>
/// The accumulated per-pixel coverage is the single source of truth for how much of a pixel a fill covers;
/// <see cref="Fill{TSink}"/>'s <c>antiAlias</c> parameter only changes how that number becomes the alpha byte
/// handed to <see cref="ICoverageSink.Span"/> - smoothed to a fractional value (the default) or thresholded to
/// fully transparent/opaque - so there is exactly one coverage algorithm regardless of the setting.
/// </para>
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

    public static void Fill<TSink>(PolygonSet polygons, bool evenOdd, IntRect clip, ref TSink sink, bool antiAlias = true)
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

        // The edge list and the per-sample working arrays are reused from one fill to the next on a thread (a page fills thousands of small shapes);
        // the scratch is taken for the duration of a fill, so a sink that fills again from inside Span gets a scratch of its own.
        var scratch = t_scratch ?? new FillScratch();
        t_scratch = null;
        try
        {
            FillCore(scratch, polygons, evenOdd, clip, minY, maxY, ref sink, antiAlias);
        }
        finally
        {
            scratch.TrimForReuse();
            t_scratch = scratch;
        }
    }

    private static void FillCore<TSink>(FillScratch scratch, PolygonSet polygons, bool evenOdd, IntRect clip, int minY, int maxY, ref TSink sink, bool antiAlias)
        where TSink : struct, ICoverageSink
    {
        var edgeCount = BuildEdges(polygons, minY, maxY, scratch);
        if (edgeCount == 0)
            return;

        var edges = scratch.Edges;
        edges.AsSpan(0, edgeCount).Sort(static (a, c) => a.YTop.CompareTo(c.YTop));

        var width = clip.Width;
        var cover = ArrayPool<int>.Shared.Rent(width + 2);
        var delta = ArrayPool<int>.Shared.Rent(width + 2);
        var alpha = ArrayPool<byte>.Shared.Rent(width + 2);
        // An edge is active at most once, so the arrays never need to hold more than the edge count.
        scratch.EnsureActiveCapacity(edgeCount);
        var active = scratch.Active;
        var activeCount = 0;
        var xs = scratch.Xs;
        var dirs = scratch.Dirs;
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
                    var sy = y + SampleOffsets[k];

                    while (nextEdge < edgeCount && edges[nextEdge].YTop <= sy)
                    {
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
                    var i = 0;
                    while (i < count)
                    {
                        var col = first + i;
                        var edgeDelta = delta[col];
                        var edgeCover = cover[col];
                        running += edgeDelta;
                        alpha[i] = ToAlpha(edgeCover + running, antiAlias);

                        cover[col] = 0;
                        delta[col] = 0;
                        i++;

                        // After a column no edge touched, the columns up to the next touched one all have the same coverage (the running sum),
                        // so their alpha is worked out once and written as a run; they are already zero in both arrays.
                        if ((edgeDelta | edgeCover) == 0 && count - i >= QuietRunMinimum)
                        {
                            var rest = count - i;
                            var run = rest;
                            var nextCover = cover.AsSpan(first + i, rest).IndexOfAnyExcept(0);
                            if (nextCover >= 0)
                                run = nextCover;
                            var nextDelta = delta.AsSpan(first + i, run).IndexOfAnyExcept(0);
                            if (nextDelta >= 0)
                                run = nextDelta;

                            if (run > 0)
                            {
                                alpha.AsSpan(i, run).Fill(ToAlpha(running, antiAlias));
                                i += run;
                            }
                        }
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

    /// <summary>
    /// The alpha byte of an accumulated coverage (out of <see cref="TotalPerPixel"/>). Same coverage number either way; only its quantization to an
    /// alpha byte differs between anti-aliased and thresholded output.
    /// </summary>
    private static byte ToAlpha(int total, bool antiAlias)
    {
        if (total > TotalPerPixel) total = TotalPerPixel;
        else if (total < 0) total = 0;

        // total is not negative here, and TotalPerPixel is 4096, so the rounded division by it is this shift.
        return antiAlias
            ? (byte)((uint)(total * 255 + TotalPerPixel / 2) >> 12)
            : total * 2 >= TotalPerPixel ? (byte)255 : (byte)0;
    }

    /// <summary>Fewer columns than this left in a row are not worth looking ahead in for a run nothing touched.</summary>
    private const int QuietRunMinimum = 16;

    /// <summary>The offset of each sub-scanline's sample from the top of its pixel row.</summary>
    private static readonly double[] SampleOffsets = CreateSampleOffsets();

    private static double[] CreateSampleOffsets()
    {
        var offsets = new double[SubScanlines];
        for (var k = 0; k < offsets.Length; k++)
            offsets[k] = (k + 0.5) / SubScanlines;
        return offsets;
    }

    [ThreadStatic]
    private static FillScratch? t_scratch;

    /// <summary>
    /// The arrays a fill works in, kept between fills on a thread. Plain arrays rather than <see cref="ArrayPool{T}"/>: the pool over this private
    /// struct is a generic instantiation over a type no precompiled code has, and a fill is the one place that needs it.
    /// </summary>
    private sealed class FillScratch
    {
        /// <summary>The most entries a scratch array keeps between fills; a larger one was made for a single enormous path and is let go.</summary>
        private const int RetainedLimit = 1 << 16;

        public Edge[] Edges = new Edge[64];
        public int[] Active = new int[64];
        public double[] Xs = new double[64];
        public int[] Dirs = new int[64];

        public void EnsureEdgeCapacity(int count)
        {
            if (Edges.Length < count)
                Edges = new Edge[Math.Max(count, Edges.Length * 2)];
        }

        public void EnsureActiveCapacity(int count)
        {
            if (Active.Length >= count)
                return;

            var size = Math.Max(count, Active.Length * 2);
            Active = new int[size];
            Xs = new double[size];
            Dirs = new int[size];
        }

        public void TrimForReuse()
        {
            if (Edges.Length > RetainedLimit)
                Edges = new Edge[64];
            if (Active.Length > RetainedLimit)
            {
                Active = new int[64];
                Xs = new double[64];
                Dirs = new int[64];
            }
        }
    }

    private static int BuildEdges(PolygonSet polygons, int minY, int maxY, FillScratch scratch)
    {
        // Each point starts at most one edge, so the point count is the most there can be.
        scratch.EnsureEdgeCapacity(polygons.PointCount);
        var edges = scratch.Edges;
        var count = 0;
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

                edges[count++] = new Edge
                {
                    YTop = y0,
                    YBottom = y1,
                    X0 = x0,
                    DxDy = (x1 - x0) / (y1 - y0),
                    Direction = dir,
                };
            }
        }

        return count;
    }
}
