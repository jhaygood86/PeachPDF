using System;
using System.Collections.Generic;
using System.Numerics;
using PeachDrawing.Core;
using PeachDrawing.PDF.Engine;

namespace PeachDrawing.PDF.Import;

/// <summary>
/// Mesh shadings (types 4 to 7: free-form and lattice triangle meshes, Coons and tensor-product patches). A canvas has no Gouraud
/// primitive, so the mesh is rasterized into an RGBA bitmap that covers the mesh's bounding box in shading space, at a resolution
/// matching how large that box is drawn; the caller draws the bitmap under the transform that carries shading space onto the canvas.
/// </summary>
internal static class PdfMeshShading
{
    const int PatchDivisions = 14;
    const int MaxPixels = 2048;

    /// <summary>A vertex of the mesh: a position in shading space and a colour (or, with a function, a parameter in <c>R</c>).</summary>
    readonly record struct Vertex(double X, double Y, double R, double G, double B, double A);

    /// <summary>Whether <paramref name="type"/> is a mesh shading this class reads.</summary>
    public static bool IsMesh(int type) => type is >= 4 and <= 7;

    /// <summary>The bitmap a mesh was rasterized into, and the rectangle of shading space it covers.</summary>
    public sealed record Bitmap(byte[] Rgba, int Width, int Height, double X0, double Y0, double X1, double Y1);

    /// <summary>
    /// Rasterizes the mesh. <paramref name="scale"/> is how many canvas units one unit of shading space is drawn as, which sets the
    /// resolution; null is returned (with a reason) for a mesh that cannot be read.
    /// </summary>
    public static Bitmap? Render(PdfSourceDocument doc, PdfDictionary shading, PdfDictionary? resources, double scale, double alpha, out string? why)
    {
        why = null;
        int type = doc.IntOf(shading.GetRaw("ShadingType"));
        if (!IsMesh(type) || shading.Stream is null) { why = "mesh shading data"; return null; }
        var space = PdfColorSpace.Resolve(doc, shading.GetRaw("ColorSpace"), resources);
        if (space is null) { why = "shading colour space"; return null; }
        var function = doc.Resolve(shading.GetRaw("Function")) is null ? null : PdfFunction.Load(doc, shading.GetRaw("Function"));
        if (doc.Resolve(shading.GetRaw("Function")) is not null && function is null) { why = "shading function"; return null; }

        int bitsCoord = doc.IntOf(shading.GetRaw("BitsPerCoordinate"));
        int bitsComp = doc.IntOf(shading.GetRaw("BitsPerComponent"));
        int bitsFlag = type == 5 ? 0 : doc.IntOf(shading.GetRaw("BitsPerFlag"));
        if (bitsCoord is < 1 or > 32 || bitsComp is < 1 or > 16 || (type != 5 && bitsFlag is < 1 or > 8)) { why = "mesh bit depths"; return null; }
        if (doc.ArrayOf(shading, "Decode") is not { } dec || dec.Elements.Count < 6) { why = "mesh decode array"; return null; }
        var decode = new double[dec.Elements.Count];
        for (int i = 0; i < decode.Length; i++) decode[i] = doc.NumberOf(dec.Elements[i]);
        int comps = function is not null ? 1 : space.Components;
        if (decode.Length < 4 + comps * 2) { why = "mesh decode array"; return null; }

        var data = doc.DecodeStream(shading).Data;
        var reader = new BitReader(data);
        double maxCoord = Math.Pow(2, bitsCoord) - 1, maxComp = Math.Pow(2, bitsComp) - 1;

        double Coord(int axis) => decode[axis * 2] + reader.Read(bitsCoord) / maxCoord * (decode[axis * 2 + 1] - decode[axis * 2]);

        double[] ReadComps()
        {
            var c = new double[comps];
            for (int i = 0; i < comps; i++) c[i] = decode[4 + i * 2] + reader.Read(bitsComp) / maxComp * (decode[5 + i * 2] - decode[4 + i * 2]);
            return c;
        }

        // A function maps a colour parameter through a lookup table; otherwise the components are converted directly.
        var lut = function is null ? null : BuildLut(function, space, alpha, decode[4], decode[5]);

        Vertex ToVertex(double x, double y, double[] c)
        {
            if (lut is not null)
            {
                double t = decode[5] == decode[4] ? 0 : (c[0] - decode[4]) / (decode[5] - decode[4]);
                return new Vertex(x, y, Math.Clamp(t, 0, 1), 0, 0, 0);
            }
            var color = space.ToColor(c, alpha);
            var (r, g, b) = color.ToRgb();
            return new Vertex(x, y, r, g, b, color.A);
        }

        var triangles = new List<(Vertex, Vertex, Vertex)>();
        switch (type)
        {
            case 4: ReadFreeForm(reader, bitsFlag, Coord, ReadComps, ToVertex, triangles); break;
            case 5: ReadLattice(reader, doc.IntOf(shading.GetRaw("VerticesPerRow")), Coord, ReadComps, ToVertex, triangles); break;
            default: ReadPatches(reader, type == 7, bitsFlag, Coord, ReadComps, ToVertex, triangles); break;
        }
        if (triangles.Count == 0) { why = "empty mesh"; return null; }

        double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue;
        foreach (var (a, b, c) in triangles)
            foreach (var v in new[] { a, b, c })
            {
                x0 = Math.Min(x0, v.X); y0 = Math.Min(y0, v.Y); x1 = Math.Max(x1, v.X); y1 = Math.Max(y1, v.Y);
            }
        if (!(x1 > x0) || !(y1 > y0)) { why = "degenerate mesh"; return null; }

        double density = Math.Clamp(scale * 2.5, 0.01, 1e6);
        int width = (int)Math.Clamp(Math.Ceiling((x1 - x0) * density), 1, MaxPixels);
        int height = (int)Math.Clamp(Math.Ceiling((y1 - y0) * density), 1, MaxPixels);
        var rgba = new byte[width * height * 4];
        double sx = width / (x1 - x0), sy = height / (y1 - y0);
        foreach (var (a, b, c) in triangles)
            Fill(rgba, width, height, x0, y1, sx, sy, a, b, c, lut);
        return new Bitmap(rgba, width, height, x0, y0, x1, y1);
    }

    // ------------------------------------------------------------ reading the mesh

    sealed class BitReader(byte[] data)
    {
        long _bit;

        public double Read(int bits)
        {
            double v = 0;
            for (int i = 0; i < bits; i++, _bit++)
            {
                int index = (int)(_bit >> 3);
                int bit = index < data.Length ? (data[index] >> (7 - (int)(_bit & 7))) & 1 : 0;
                v = v * 2 + bit;
            }
            return v;
        }

        public bool AtEnd => (_bit >> 3) >= data.Length;

        public void AlignToByte() => _bit = (_bit + 7) & ~7L;
    }

    static void ReadFreeForm(BitReader r, int bitsFlag, Func<int, double> coord, Func<double[]> comps, Func<double, double, double[], Vertex> make,
        List<(Vertex, Vertex, Vertex)> triangles)
    {
        Vertex? a = null, b = null, c = null;
        while (!r.AtEnd)
        {
            int flag = (int)r.Read(bitsFlag);
            double x = coord(0), y = coord(1);
            var v = make(x, y, comps());
            r.AlignToByte();
            if (flag == 0 || a is null)
            {
                // A new triangle takes this and the next two vertices (their flags are ignored).
                var second = ReadVertex(r, bitsFlag, coord, comps, make);
                var third = ReadVertex(r, bitsFlag, coord, comps, make);
                if (second is null || third is null) return;
                (a, b, c) = (v, second, third);
            }
            else if (flag == 1) (a, b, c) = (b!.Value, c!.Value, v);
            else (a, b, c) = (a.Value, c!.Value, v);
            triangles.Add((a.Value, b!.Value, c!.Value));
        }
    }

    static Vertex? ReadVertex(BitReader r, int bitsFlag, Func<int, double> coord, Func<double[]> comps, Func<double, double, double[], Vertex> make)
    {
        if (r.AtEnd) return null;
        r.Read(bitsFlag);
        double x = coord(0), y = coord(1);
        var v = make(x, y, comps());
        r.AlignToByte();
        return v;
    }

    static void ReadLattice(BitReader r, int perRow, Func<int, double> coord, Func<double[]> comps, Func<double, double, double[], Vertex> make,
        List<(Vertex, Vertex, Vertex)> triangles)
    {
        if (perRow < 2) return;
        var previous = new List<Vertex>();
        while (!r.AtEnd)
        {
            var row = new List<Vertex>(perRow);
            for (int i = 0; i < perRow && !r.AtEnd; i++)
            {
                double x = coord(0), y = coord(1);
                row.Add(make(x, y, comps()));
                r.AlignToByte();
            }
            if (row.Count < perRow) break;
            if (previous.Count == perRow)
                for (int i = 0; i + 1 < perRow; i++)
                {
                    triangles.Add((previous[i], previous[i + 1], row[i]));
                    triangles.Add((previous[i + 1], row[i + 1], row[i]));
                }
            previous = row;
        }
    }

    static void ReadPatches(BitReader r, bool tensor, int bitsFlag, Func<int, double> coord, Func<double[]> comps, Func<double, double, double[], Vertex> make,
        List<(Vertex, Vertex, Vertex)> triangles)
    {
        var p = new (double X, double Y)[4, 4];
        var c = new double[4][];                   // corner colours c11, c14, c44, c41
        var prevP = new (double X, double Y)[4, 4];
        double[][]? prevC = null;
        int pointCount = tensor ? 16 : 12;
        while (!r.AtEnd)
        {
            int flag = (int)r.Read(bitsFlag);
            // The 12 boundary points in spec order p11 p12 p13 p14 p24 p34 p44 p43 p42 p41 p31 p21, then (tensor) p22 p23 p33 p32.
            (int I, int J)[] order = [(0, 0), (0, 1), (0, 2), (0, 3), (1, 3), (2, 3), (3, 3), (3, 2), (3, 1), (3, 0), (2, 0), (1, 0), (1, 1), (1, 2), (2, 2), (2, 1)];
            int first = 0;
            if (flag != 0 && prevC is not null)
            {
                // The new patch shares an edge with the previous one: its first four points and two colours come from there.
                (int I, int J)[] edge = flag switch
                {
                    1 => [(0, 3), (1, 3), (2, 3), (3, 3)],
                    2 => [(3, 3), (3, 2), (3, 1), (3, 0)],
                    _ => [(3, 0), (2, 0), (1, 0), (0, 0)],
                };
                for (int k = 0; k < 4; k++) p[order[k].I, order[k].J] = prevP[edge[k].I, edge[k].J];
                (c[0], c[1]) = flag switch { 1 => (prevC[1], prevC[2]), 2 => (prevC[2], prevC[3]), _ => (prevC[3], prevC[0]) };
                first = 4;
            }
            else flag = 0;
            for (int k = first; k < pointCount; k++)
            {
                double x = coord(0), y = coord(1);
                p[order[k].I, order[k].J] = (x, y);
            }
            for (int k = flag == 0 ? 0 : 2; k < 4; k++) c[k] = comps();
            r.AlignToByte();

            if (!tensor) AddCoonsInterior(p);
            var colours = new Vertex[4];
            for (int k = 0; k < 4; k++) colours[k] = make(0, 0, c[k]);
            Tessellate(p, colours, triangles);

            Array.Copy(p, prevP, p.Length);
            prevC = [c[0], c[1], c[2], c[3]];
            c = new double[4][];
            // The corner colours of the new patch must start from the saved ones for edge-sharing flags; they are refilled above.
        }
    }

    /// <summary>Fills in the four inner control points of a Coons patch so it can be treated as a tensor-product patch (PDF 8.7.4.5.7).</summary>
    static void AddCoonsInterior((double X, double Y)[,] p)
    {
        double Comp(Func<(double X, double Y), double> f, int[] w, (int I, int J)[] pts)
        {
            double sum = 0;
            for (int i = 0; i < w.Length; i++) sum += w[i] * f(p[pts[i].I, pts[i].J]);
            return sum / 9;
        }
        // p22, p23, p32, p33 with the spec's weights over p11 p12 p21 p14 p41 p42 p24 p44 and so on, in 0-based grid indexes.
        (int, int)[] A = [(0, 0), (0, 1), (1, 0), (0, 3), (3, 0), (3, 1), (1, 3), (3, 3)];
        int[] wa = [-4, 6, 6, -2, -2, 3, 3, -1];
        (int, int)[] B = [(0, 3), (0, 2), (1, 3), (0, 0), (3, 3), (3, 2), (1, 0), (3, 0)];
        (int, int)[] C = [(3, 0), (3, 1), (2, 0), (3, 3), (0, 0), (0, 1), (2, 3), (0, 3)];
        (int, int)[] D = [(3, 3), (3, 2), (2, 3), (3, 0), (0, 3), (0, 2), (2, 0), (0, 0)];
        p[1, 1] = (Comp(t => t.X, wa, A), Comp(t => t.Y, wa, A));
        p[1, 2] = (Comp(t => t.X, wa, B), Comp(t => t.Y, wa, B));
        p[2, 1] = (Comp(t => t.X, wa, C), Comp(t => t.Y, wa, C));
        p[2, 2] = (Comp(t => t.X, wa, D), Comp(t => t.Y, wa, D));
    }

    static void Tessellate((double X, double Y)[,] p, Vertex[] corner, List<(Vertex, Vertex, Vertex)> triangles)
    {
        int n = PatchDivisions;
        var grid = new Vertex[n + 1, n + 1];
        for (int a = 0; a <= n; a++)
            for (int b = 0; b <= n; b++)
            {
                double u = a / (double)n, v = b / (double)n;
                double x = 0, y = 0;
                for (int i = 0; i < 4; i++)
                    for (int j = 0; j < 4; j++)
                    {
                        double w = Bernstein(i, u) * Bernstein(j, v);
                        x += w * p[i, j].X;
                        y += w * p[i, j].Y;
                    }
                // Corner colours c11 c14 c44 c41 sit at (u,v) = (0,0) (0,1) (1,1) (1,0).
                double w11 = (1 - u) * (1 - v), w14 = (1 - u) * v, w44 = u * v, w41 = u * (1 - v);
                grid[a, b] = new Vertex(x, y,
                    w11 * corner[0].R + w14 * corner[1].R + w44 * corner[2].R + w41 * corner[3].R,
                    w11 * corner[0].G + w14 * corner[1].G + w44 * corner[2].G + w41 * corner[3].G,
                    w11 * corner[0].B + w14 * corner[1].B + w44 * corner[2].B + w41 * corner[3].B,
                    w11 * corner[0].A + w14 * corner[1].A + w44 * corner[2].A + w41 * corner[3].A);
            }
        for (int a = 0; a < n; a++)
            for (int b = 0; b < n; b++)
            {
                triangles.Add((grid[a, b], grid[a + 1, b], grid[a, b + 1]));
                triangles.Add((grid[a + 1, b], grid[a + 1, b + 1], grid[a, b + 1]));
            }
    }

    static double Bernstein(int i, double t)
    {
        double s = 1 - t;
        return i switch { 0 => s * s * s, 1 => 3 * t * s * s, 2 => 3 * t * t * s, _ => t * t * t };
    }

    // ------------------------------------------------------------ rasterizing

    static (byte R, byte G, byte B, byte A)[] BuildLut(PdfFunction function, PdfColorSpace space, double alpha, double t0, double t1)
    {
        var lut = new (byte, byte, byte, byte)[256];
        double d0 = function.DomainStart, d1 = function.DomainEnd;
        for (int i = 0; i < 256; i++)
        {
            var values = function.Evaluate(d0 + i / 255.0 * (d1 - d0));
            var comps = new double[space.Components];
            for (int k = 0; k < comps.Length; k++) comps[k] = k < values.Length ? values[k] : 0;
            var color = space.ToColor(comps, alpha);
            var (r, g, b) = color.ToRgb();
            lut[i] = (r, g, b, color.A);
        }
        return lut;
    }

    static void Fill(byte[] rgba, int width, int height, double originX, double topY, double sx, double sy, Vertex a, Vertex b, Vertex c,
        (byte R, byte G, byte B, byte A)[]? lut)
    {
        // Vertices in pixel space (y down from the bitmap's top).
        double ax = (a.X - originX) * sx, ay = (topY - a.Y) * sy, bx = (b.X - originX) * sx, by = (topY - b.Y) * sy, cx = (c.X - originX) * sx, cy = (topY - c.Y) * sy;
        double area = (bx - ax) * (cy - ay) - (by - ay) * (cx - ax);
        if (Math.Abs(area) < 1e-12) return;
        int minX = Math.Max(0, (int)Math.Floor(Math.Min(ax, Math.Min(bx, cx)) - 1)), maxX = Math.Min(width - 1, (int)Math.Ceiling(Math.Max(ax, Math.Max(bx, cx)) + 1));
        int minY = Math.Max(0, (int)Math.Floor(Math.Min(ay, Math.Min(by, cy)) - 1)), maxY = Math.Min(height - 1, (int)Math.Ceiling(Math.Max(ay, Math.Max(by, cy)) + 1));
        // A little slack so neighbouring triangles leave no hairline gaps between them.
        double eps = -0.02;
        for (int y = minY; y <= maxY; y++)
            for (int x = minX; x <= maxX; x++)
            {
                double px = x + 0.5, py = y + 0.5;
                double w0 = ((bx - px) * (cy - py) - (by - py) * (cx - px)) / area;
                double w1 = ((cx - px) * (ay - py) - (cy - py) * (ax - px)) / area;
                double w2 = 1 - w0 - w1;
                if (w0 < eps || w1 < eps || w2 < eps) continue;
                int o = (y * width + x) * 4;
                if (lut is not null)
                {
                    double t = Math.Clamp(w0 * a.R + w1 * b.R + w2 * c.R, 0, 1);
                    var (r, g, bl, al) = lut[(int)Math.Round(t * 255)];
                    (rgba[o], rgba[o + 1], rgba[o + 2], rgba[o + 3]) = (r, g, bl, al);
                }
                else
                {
                    rgba[o] = (byte)Math.Clamp(Math.Round(w0 * a.R + w1 * b.R + w2 * c.R), 0, 255);
                    rgba[o + 1] = (byte)Math.Clamp(Math.Round(w0 * a.G + w1 * b.G + w2 * c.G), 0, 255);
                    rgba[o + 2] = (byte)Math.Clamp(Math.Round(w0 * a.B + w1 * b.B + w2 * c.B), 0, 255);
                    rgba[o + 3] = (byte)Math.Clamp(Math.Round(w0 * a.A + w1 * b.A + w2 * c.A), 0, 255);
                }
            }
    }
}
