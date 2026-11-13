// Derived from PDFsharp (MIT) - see LICENSE.md

using System.Numerics;
using PeachDrawing.Core;
using PeachDrawing.PDF.Engine.Drawing;
using PeachDrawing.PDF.Engine.Rendering;
using PeachDrawing.PDF.Engine.Internal;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace PeachDrawing.PDF.Engine.Advanced;

/// <summary>
/// The vertex data of a conic gradient's Type 4 Gouraud-shaded triangle mesh, and the color interpolation it samples with.
/// </summary>
internal static class PdfConicMesh
{
    internal static byte[] BuildConicMeshData(PaintColor[] colors, double[] angles,
        double cx, double cy, double R,
        double xMin, double xMax, double yMin, double yMax,
        bool cmyk, bool alphaOnly)
    {
        var data = new System.Collections.Generic.List<byte>(4096);

        for (int s = 0; s < colors.Length - 1; s++)
        {
            double a1 = angles[s], a2 = angles[s + 1];
            PaintColor c1 = colors[s], c2 = colors[s + 1];
            int nTri = Math.Max(1, (int)Math.Ceiling(Math.Abs(a2 - a1) * (180.0 / Math.PI)));
            if (nTri > 180) nTri = 180;

            for (int t = 0; t < nTri; t++)
            {
                double tA = (double)t / nTri;
                double tB = (double)(t + 1) / nTri;
                double angleA = a1 + (a2 - a1) * tA;
                double angleB = a1 + (a2 - a1) * tB;

                PaintColor colorA = cmyk ? LerpXColorCmyk(c1, c2, tA) : LerpXColor(c1, c2, tA);
                PaintColor colorB = cmyk ? LerpXColorCmyk(c1, c2, tB) : LerpXColor(c1, c2, tB);

                // In PDF view space: 0=top, clockwise: x=sin(angle), y=+cos(angle)
                double oAX = cx + R * Math.Sin(angleA);
                double oAY = cy + R * Math.Cos(angleA);
                double oBX = cx + R * Math.Sin(angleB);
                double oBY = cy + R * Math.Cos(angleB);

                AppendConicVertex(data, 0, cx, cy, colorA, xMin, xMax, yMin, yMax, cmyk, alphaOnly);
                AppendConicVertex(data, 0, oAX, oAY, colorA, xMin, xMax, yMin, yMax, cmyk, alphaOnly);
                AppendConicVertex(data, 0, oBX, oBY, colorB, xMin, xMax, yMin, yMax, cmyk, alphaOnly);
            }
        }

        return [.. data];
    }

    internal static void AppendConicVertex(System.Collections.Generic.List<byte> data, int flag,
        double x, double y, PaintColor color,
        double xMin, double xMax, double yMin, double yMax,
        bool cmyk, bool alphaOnly)
    {
        data.Add((byte)flag);
        AppendUInt32BE(data, EncodeCoordinate(x, xMin, xMax));
        AppendUInt32BE(data, EncodeCoordinate(y, yMin, yMax));
        if (alphaOnly)
        {
            data.Add(color.A);
        }
        else if (cmyk)
        {
            var (cyan, magenta, yellow, black) = color.ToCmyk();
            data.Add((byte)Math.Round(Math.Clamp(cyan, 0, 1) * 255));
            data.Add((byte)Math.Round(Math.Clamp(magenta, 0, 1) * 255));
            data.Add((byte)Math.Round(Math.Clamp(yellow, 0, 1) * 255));
            data.Add((byte)Math.Round(Math.Clamp(black, 0, 1) * 255));
        }
        else
        {
            var (red, green, blue) = color.ToRgb();
            data.Add(red);
            data.Add(green);
            data.Add(blue);
        }
    }

    internal static uint EncodeCoordinate(double val, double min, double max)
    {
        if (max <= min) return 0;
        double t = Math.Clamp((val - min) / (max - min), 0.0, 1.0);
        return (uint)Math.Round(t * 4294967295.0);
    }

    internal static void AppendUInt32BE(System.Collections.Generic.List<byte> data, uint value)
    {
        data.Add((byte)(value >> 24));
        data.Add((byte)(value >> 16));
        data.Add((byte)(value >> 8));
        data.Add((byte)(value & 0xFF));
    }

    internal static PaintColor LerpXColor(PaintColor a, PaintColor b, double t)
    {
        t = Math.Clamp(t, 0.0, 1.0);
        var (aRed, aGreen, aBlue) = a.ToRgb();
        var (bRed, bGreen, bBlue) = b.ToRgb();
        return PaintColor.FromArgb(
            (int)Math.Round(a.Opacity() * 255 + t * (b.Opacity() - a.Opacity()) * 255),
            (int)Math.Round(aRed + t * (bRed - aRed)),
            (int)Math.Round(aGreen + t * (bGreen - aGreen)),
            (int)Math.Round(aBlue + t * (bBlue - aBlue)));
    }

    /// <summary>
    /// CMYK counterpart of <see cref="LerpXColor"/> - interpolates C/M/Y/K directly rather than R/G/B
    /// (meaningless for a CMYK-tagged <see cref="PaintColor"/>), used for a conic gradient whose stops are
    /// all CMYK-tagged (see <see cref="PdfShading"/>'s color-mode resolution).
    /// </summary>
    internal static PaintColor LerpXColorCmyk(PaintColor a, PaintColor b, double t)
    {
        t = Math.Clamp(t, 0.0, 1.0);
        var (aCyan, aMagenta, aYellow, aBlack) = a.ToCmyk();
        var (bCyan, bMagenta, bYellow, bBlack) = b.ToCmyk();
        return PdfColors.FromCmyk(
            a.Opacity() + t * (b.Opacity() - a.Opacity()),
            aCyan + t * (bCyan - aCyan),
            aMagenta + t * (bMagenta - aMagenta),
            aYellow + t * (bYellow - aYellow),
            aBlack + t * (bBlack - aBlack));
    }
}
