using System;
using System.Numerics;

namespace PeachBrackets.Scripting.Internal.Canvas;

/// <summary>
/// A 2D affine transform in double precision, in the layout of <see cref="Matrix3x2"/> (a row vector times the matrix: <c>x' = x*A + y*C + E</c>, <c>y' = x*B + y*D + F</c>, so
/// <c>a * b</c> applies <c>a</c> first). A canvas's current transform is kept in this, not in the single-precision <see cref="Matrix3x2"/>, so that a scale of
/// <see cref="double.MaxValue"/> is a number a path can still be taken back from.
/// </summary>
internal readonly record struct Affine2D(double A, double B, double C, double D, double E, double F)
{
    public static Affine2D Identity { get; } = new(1, 0, 0, 1, 0, 0);

    public static Affine2D CreateScale(double x, double y) => new(x, 0, 0, y, 0, 0);

    public static Affine2D CreateTranslation(double x, double y) => new(1, 0, 0, 1, x, y);

    public static Affine2D CreateRotation(double radians)
    {
        var (sin, cos) = Math.SinCos(radians);
        return new Affine2D(cos, sin, -sin, cos, 0, 0);
    }

    public static Affine2D From(Matrix3x2 m) => new(m.M11, m.M12, m.M21, m.M22, m.M31, m.M32);

    /// <summary>The single-precision matrix a backend draws with.</summary>
    public Matrix3x2 ToMatrix3x2() => new((float)A, (float)B, (float)C, (float)D, (float)E, (float)F);

    /// <summary>The transform that applies <paramref name="first"/> and then <paramref name="second"/>.</summary>
    public static Affine2D operator *(Affine2D first, Affine2D second) => new(
        first.A * second.A + first.B * second.C, first.A * second.B + first.B * second.D,
        first.C * second.A + first.D * second.C, first.C * second.B + first.D * second.D,
        first.E * second.A + first.F * second.C + second.E, first.E * second.B + first.F * second.D + second.F);

    public (double X, double Y) Apply(double x, double y) => (x * A + y * C + E, x * B + y * D + F);

    /// <summary>The transform that undoes this one, or false when there is none (it flattens the plane, or a number is not finite). Scaled first, so a determinant that does not fit a double does not stand in the way.</summary>
    public bool TryInvert(out Affine2D inverse)
    {
        inverse = default;
        var largest = Math.Max(Math.Max(Math.Abs(A), Math.Abs(B)), Math.Max(Math.Abs(C), Math.Abs(D)));
        if (!double.IsFinite(largest) || largest == 0 || !double.IsFinite(E) || !double.IsFinite(F))
            return false;

        var (a, b, c, d) = (A / largest, B / largest, C / largest, D / largest);
        var determinant = a * d - b * c;
        if (determinant == 0 || !double.IsFinite(determinant))
            return false;

        var (ia, ib, ic, id) = (d / determinant / largest, -b / determinant / largest, -c / determinant / largest, a / determinant / largest);
        inverse = new Affine2D(ia, ib, ic, id, -(E * ia + F * ic), -(E * ib + F * id));
        return true;
    }

    public bool IsInvertible => TryInvert(out _);
}
