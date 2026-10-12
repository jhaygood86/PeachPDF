using PeachDrawing.Core;
using System;
using System.Numerics;

namespace PeachPDF.PDF.Drawing;

/// <summary>
/// Builds and composes the 2D affine transforms the PDF layer works with. The matrix type itself is
/// <see cref="Matrix3x2"/>, the same one <see cref="Canvas"/> uses; points are still transformed in
/// <see langword="double"/> precision so coordinates are not rounded to single precision.
/// </summary>
internal static class PdfMatrix
{
    /// <summary>Creates a matrix from double-precision elements, rounding each to single precision.</summary>
    public static Matrix3x2 Create(double m11, double m12, double m21, double m22, double offsetX, double offsetY)
        => new((float)m11, (float)m12, (float)m21, (float)m22, (float)offsetX, (float)offsetY);

    /// <summary>The matrix <see cref="Matrix3x2.Identity"/> composed with a translation.</summary>
    public static Matrix3x2 Translation(double offsetX, double offsetY)
        => new(1f, 0f, 0f, 1f, (float)offsetX, (float)offsetY);

    /// <summary>A scaling about the origin.</summary>
    public static Matrix3x2 Scaling(double scaleX, double scaleY)
        => new((float)scaleX, 0f, 0f, (float)scaleY, 0f, 0f);
}

internal static class PdfMatrixExtensions
{
    /// <summary>Applies the matrix to a point, multiplying in double precision.</summary>
    public static PaintPoint Transform(this Matrix3x2 matrix, PaintPoint point)
        => new(point.X * matrix.M11 + point.Y * matrix.M21 + matrix.M31,
               point.X * matrix.M12 + point.Y * matrix.M22 + matrix.M32);
}
