using System;

namespace PeachPDF.Raster;

/// <summary>
/// A 2D affine transform in the same row-vector convention as <c>RMatrix</c>/<c>XMatrix</c>:
/// <c>x' = x * M11 + y * M21 + OffsetX</c>, <c>y' = x * M12 + y * M22 + OffsetY</c>. Kept as its own
/// small struct (rather than reusing <c>XMatrix</c>) so the raster backend has no dependency on the
/// PDF writer's drawing types.
/// </summary>
internal readonly record struct Affine(double M11, double M12, double M21, double M22, double OffsetX, double OffsetY)
{
    public static Affine Identity { get; } = new(1, 0, 0, 1, 0, 0);

    public bool IsIdentity => M11 == 1 && M12 == 0 && M21 == 0 && M22 == 1 && OffsetX == 0 && OffsetY == 0;

    /// <summary>True when the transform keeps axis-aligned rectangles axis-aligned (no rotation or skew).</summary>
    public bool IsAxisAligned => M12 == 0 && M21 == 0;

    public double Determinant => M11 * M22 - M12 * M21;

    public static Affine Scale(double sx, double sy) => new(sx, 0, 0, sy, 0, 0);

    /// <summary>Applies <paramref name="first"/> and then <paramref name="second"/> to a point.</summary>
    public static Affine Then(in Affine first, in Affine second) => new(
        first.M11 * second.M11 + first.M12 * second.M21,
        first.M11 * second.M12 + first.M12 * second.M22,
        first.M21 * second.M11 + first.M22 * second.M21,
        first.M21 * second.M12 + first.M22 * second.M22,
        first.OffsetX * second.M11 + first.OffsetY * second.M21 + second.OffsetX,
        first.OffsetX * second.M12 + first.OffsetY * second.M22 + second.OffsetY);

    public (double X, double Y) Apply(double x, double y) =>
        (x * M11 + y * M21 + OffsetX, x * M12 + y * M22 + OffsetY);

    /// <summary>The inverse transform, or null when the matrix is singular.</summary>
    public Affine? Invert()
    {
        var det = Determinant;
        if (Math.Abs(det) < 1e-300 || double.IsNaN(det) || double.IsInfinity(det))
            return null;

        var inv = 1.0 / det;
        var m11 = M22 * inv;
        var m12 = -M12 * inv;
        var m21 = -M21 * inv;
        var m22 = M11 * inv;
        return new Affine(m11, m12, m21, m22,
            -(OffsetX * m11 + OffsetY * m21),
            -(OffsetX * m12 + OffsetY * m22));
    }

    /// <summary>The larger of the two axis scale factors of the linear part.</summary>
    public double MaxScale
    {
        get
        {
            var sx = Math.Sqrt(M11 * M11 + M12 * M12);
            var sy = Math.Sqrt(M21 * M21 + M22 * M22);
            return Math.Max(sx, sy);
        }
    }
}

/// <summary>An integer pixel rectangle; the right and bottom edges are exclusive.</summary>
internal readonly record struct IntRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;

    public int Height => Bottom - Top;

    public bool IsEmpty => Right <= Left || Bottom <= Top;

    public static IntRect Empty => new(0, 0, 0, 0);

    public IntRect Intersect(in IntRect other)
    {
        var l = Math.Max(Left, other.Left);
        var t = Math.Max(Top, other.Top);
        var r = Math.Min(Right, other.Right);
        var b = Math.Min(Bottom, other.Bottom);
        return r <= l || b <= t ? Empty : new IntRect(l, t, r, b);
    }
}
