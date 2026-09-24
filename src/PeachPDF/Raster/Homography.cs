using System;
using System.Collections.Generic;
using System.Numerics;

namespace PeachPDF.Raster;

/// <summary>
/// A projective map of the plane, a 3x3 matrix acting on the column vector (x, y, 1): the picture a 4x4 CSS transform (perspective
/// included) makes of a flat element. Where an <see cref="Affine"/> keeps parallel lines parallel, this does not; the third row is
/// what makes the difference (the divisor <c>W</c>).
/// </summary>
internal readonly struct Homography(double m11, double m12, double m13, double m21, double m22, double m23, double m31, double m32, double m33)
{
    public double M11 { get; } = m11;
    public double M12 { get; } = m12;
    public double M13 { get; } = m13;
    public double M21 { get; } = m21;
    public double M22 { get; } = m22;
    public double M23 { get; } = m23;
    public double M31 { get; } = m31;
    public double M32 { get; } = m32;
    public double M33 { get; } = m33;

    /// <summary>Below this the third row is treated as zero: the map is affine, and the cheaper path applies.</summary>
    private const double AffineTolerance = 1e-8;

    public static Homography Identity { get; } = new(1, 0, 0, 0, 1, 0, 0, 0, 1);

    public static Homography Translation(double dx, double dy) => new(1, 0, dx, 0, 1, dy, 0, 0, 1);

    /// <summary>
    /// What a 4x4 (System.Numerics, row-vector convention) does to the plane z = 0: the points (x, y, 0, 1) drop their z and
    /// divide by the fourth coordinate.
    /// </summary>
    public static Homography FromMatrix4(in Matrix4x4 m) => new(m.M11, m.M21, m.M41, m.M12, m.M22, m.M42, m.M14, m.M24, m.M44);

    /// <summary>True when the third row does nothing, so straight lines stay parallel and a PDF <c>cm</c> can carry the map.</summary>
    public bool IsAffine => Math.Abs(M31) < AffineTolerance && Math.Abs(M32) < AffineTolerance;

    /// <summary>The homogeneous image of (<paramref name="x"/>, <paramref name="y"/>): X, Y and the divisor W.</summary>
    public (double X, double Y, double W) ApplyHomogeneous(double x, double y) =>
        (M11 * x + M12 * y + M13, M21 * x + M22 * y + M23, M31 * x + M32 * y + M33);

    /// <summary>The image of a point, or null when it maps to infinity or behind the viewer (W not positive).</summary>
    public (double X, double Y)? Apply(double x, double y)
    {
        var (px, py, w) = ApplyHomogeneous(x, y);
        return w > 1e-9 ? (px / w, py / w) : null;
    }

    /// <summary>The map that does <paramref name="first"/> and then <paramref name="second"/>.</summary>
    public static Homography Then(in Homography first, in Homography second) => new(
        second.M11 * first.M11 + second.M12 * first.M21 + second.M13 * first.M31,
        second.M11 * first.M12 + second.M12 * first.M22 + second.M13 * first.M32,
        second.M11 * first.M13 + second.M12 * first.M23 + second.M13 * first.M33,
        second.M21 * first.M11 + second.M22 * first.M21 + second.M23 * first.M31,
        second.M21 * first.M12 + second.M22 * first.M22 + second.M23 * first.M32,
        second.M21 * first.M13 + second.M22 * first.M23 + second.M23 * first.M33,
        second.M31 * first.M11 + second.M32 * first.M21 + second.M33 * first.M31,
        second.M31 * first.M12 + second.M32 * first.M22 + second.M33 * first.M32,
        second.M31 * first.M13 + second.M32 * first.M23 + second.M33 * first.M33);

    /// <summary>The same map scaled by -1, which is the same picture: used to make W positive where the element is.</summary>
    public Homography Negated() => new(-M11, -M12, -M13, -M21, -M22, -M23, -M31, -M32, -M33);

    public Homography? Invert()
    {
        var c11 = M22 * M33 - M23 * M32;
        var c12 = M23 * M31 - M21 * M33;
        var c13 = M21 * M32 - M22 * M31;
        var determinant = M11 * c11 + M12 * c12 + M13 * c13;
        if (!(Math.Abs(determinant) > 1e-300) || double.IsNaN(determinant) || double.IsInfinity(determinant))
            return null;

        var inverse = 1.0 / determinant;
        return new Homography(
            c11 * inverse,
            (M13 * M32 - M12 * M33) * inverse,
            (M12 * M23 - M13 * M22) * inverse,
            c12 * inverse,
            (M11 * M33 - M13 * M31) * inverse,
            (M13 * M21 - M11 * M23) * inverse,
            c13 * inverse,
            (M12 * M31 - M11 * M32) * inverse,
            (M11 * M22 - M12 * M21) * inverse);
    }

    /// <summary>
    /// The image of the rectangle (<paramref name="left"/>, <paramref name="top"/>, <paramref name="right"/>, <paramref name="bottom"/>) as
    /// a polygon, cut off where it would reach the viewer's plane (W at or below <paramref name="epsilon"/>) before dividing, so the part
    /// of an element that passes behind the eye never projects to a smear across the page. Empty when none of it is in front.
    /// </summary>
    public List<(double X, double Y)> ProjectRectangle(double left, double top, double right, double bottom, double epsilon = 1e-4)
    {
        var corners = new (double X, double Y, double W)[]
        {
            ApplyHomogeneous(left, top), ApplyHomogeneous(right, top), ApplyHomogeneous(right, bottom), ApplyHomogeneous(left, bottom),
        };

        var clipped = new List<(double X, double Y, double W)>(6);
        for (var i = 0; i < corners.Length; i++)
        {
            var a = corners[i];
            var b = corners[(i + 1) % corners.Length];
            var aInside = a.W > epsilon;
            var bInside = b.W > epsilon;
            if (aInside)
                clipped.Add(a);

            if (aInside != bInside)
            {
                var t = (epsilon - a.W) / (b.W - a.W);
                clipped.Add((a.X + t * (b.X - a.X), a.Y + t * (b.Y - a.Y), epsilon));
            }
        }

        var result = new List<(double X, double Y)>(clipped.Count);
        foreach (var (x, y, w) in clipped)
            result.Add((x / w, y / w));

        return result;
    }
}
