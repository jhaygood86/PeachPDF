// Derived from PDFsharp (MIT) - see LICENSE.md

using PeachDrawing.Core;
using PeachDrawing.PDF.Engine.Drawing;
using PeachDrawing.PDF.Engine.Internal;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace PeachDrawing.PDF.Engine.Advanced;

/// <summary>
/// The PDF functions a gradient shading's colors and alpha are built from: Type 2 interpolation between two stops and the Type 3
/// stitching function that joins one per stop pair (with solid pads where the stops stop short of the ends).
/// </summary>
internal static class PdfShadingFunctions
{
    /// <summary>
    /// One slice of a PDF Type 3 stitching function's domain: a plain Type 2 function
    /// interpolating between <paramref name="LeftIndex"/> and <paramref name="RightIndex"/>
    /// (the same index for both is a solid-color pad segment, see <see cref="BuildStitchSegments"/>).
    /// </summary>
    internal readonly record struct StitchSegment(double Lo, double Hi, int LeftIndex, int RightIndex);

    /// <summary>
    /// A gradient's colors interpolate strictly between its first and last stop positions; CSS
    /// requires the color at (and beyond) each end stop to hold solid outside that range (CSS
    /// Images 4 §3.5.5) rather than the interpolation stretching to fill the shading's whole
    /// [0,1] domain. Splits <paramref name="positions"/> into the segments a Type 3 stitching
    /// function needs to reproduce that: an optional leading solid-<c>colors[0]</c> pad when
    /// <c>positions[0] &gt; 0</c>, one interpolating segment per adjacent stop pair, and an
    /// optional trailing solid-<c>colors[^1]</c> pad when <c>positions[^1] &lt; 1</c>.
    /// </summary>
    internal static List<StitchSegment> BuildStitchSegments(double[] positions)
    {
        const double eps = 1e-6;
        int n = positions.Length;
        var segments = new List<StitchSegment>(n + 1);

        if (positions[0] > eps)
            segments.Add(new StitchSegment(0.0, positions[0], 0, 0));

        for (int i = 0; i < n - 1; i++)
            segments.Add(new StitchSegment(positions[i], positions[i + 1], i, i + 1));

        if (positions[n - 1] < 1.0 - eps)
            segments.Add(new StitchSegment(positions[n - 1], 1.0, n - 1, n - 1));

        return segments;
    }

    /// <summary>
    /// Whether a gradient's stops need the full Type 3 stitching-function machinery rather than
    /// the cheaper plain Type 2 (single C0-to-C1 interpolation across the whole [0,1] domain)
    /// shortcut - true whenever there are more than 2 stops, or the first/last stop isn't
    /// already sitting at the domain edge (0/1) where the shortcut is exact.
    /// </summary>
    internal static bool RequiresStitchingFunction(double[] positions)
    {
        const double eps = 1e-6;
        return positions.Length > 2 || positions[0] > eps || positions[^1] < 1.0 - eps;
    }

    internal static string BuildBoundsLiteral(IReadOnlyList<StitchSegment> segments)
    {
        var bounds = new StringBuilder("[");
        for (int i = 0; i < segments.Count - 1; i++)
        {
            if (i > 0) bounds.Append(' ');
            bounds.Append(segments[i].Hi.ToString("G6", CultureInfo.InvariantCulture));
        }
        bounds.Append(']');
        return bounds.ToString();
    }

    internal static string BuildEncodeLiteral(int segmentCount)
    {
        var encode = new StringBuilder("[");
        for (int i = 0; i < segmentCount; i++)
        {
            if (i > 0) encode.Append(' ');
            encode.Append("0 1");
        }
        encode.Append(']');
        return encode.ToString();
    }

    internal static PdfDictionary BuildAlphaStitchingFunction(PaintColor[] colors, double[] positions)
    {
        var segments = BuildStitchSegments(positions);
        var stitching = new PdfDictionary();
        stitching.Elements["/FunctionType"] = new PdfInteger(3);
        stitching.Elements["/Domain"] = new PdfLiteral("[0 1]");
        stitching.Elements["/Bounds"] = new PdfLiteral(BuildBoundsLiteral(segments));
        stitching.Elements["/Encode"] = new PdfLiteral(BuildEncodeLiteral(segments.Count));

        var functions = new PdfArray();
        foreach (var seg in segments)
        {
            var fn = new PdfDictionary();
            fn.Elements["/FunctionType"] = new PdfInteger(2);
            fn.Elements["/Domain"] = new PdfLiteral("[0 1]");
            fn.Elements["/C0"] = new PdfLiteral("[" + AlphaToString(colors[seg.LeftIndex].Opacity()) + "]");
            fn.Elements["/C1"] = new PdfLiteral("[" + AlphaToString(colors[seg.RightIndex].Opacity()) + "]");
            fn.Elements["/N"] = new PdfInteger(1);
            functions.Elements.Add(fn);
        }
        stitching.Elements["/Functions"] = functions;
        return stitching;
    }

    internal static string AlphaToString(double a) =>
        a.ToString("0.###", CultureInfo.InvariantCulture);

    internal static PdfDictionary BuildStitchingFunction(PaintColor[] colors, double[] positions, PdfColorMode colorMode)
    {
        var segments = BuildStitchSegments(positions);
        var stitching = new PdfDictionary();
        stitching.Elements["/FunctionType"] = new PdfInteger(3);
        stitching.Elements["/Domain"] = new PdfLiteral("[0 1]");
        stitching.Elements["/Bounds"] = new PdfLiteral(BuildBoundsLiteral(segments));
        stitching.Elements["/Encode"] = new PdfLiteral(BuildEncodeLiteral(segments.Count));

        // Sub-functions: one Type 2 per segment (interpolating between two real stops, or a
        // solid-color pad at either end - see BuildStitchSegments).
        var functions = new PdfArray();
        foreach (var seg in segments)
        {
            PaintColor c0 = ColorSpaceHelper.EnsureColorMode(colorMode, colors[seg.LeftIndex]);
            PaintColor c1 = ColorSpaceHelper.EnsureColorMode(colorMode, colors[seg.RightIndex]);
            var fn = new PdfDictionary();
            fn.Elements["/FunctionType"] = new PdfInteger(2);
            fn.Elements["/Domain"] = new PdfLiteral("[0 1]");
            fn.Elements["/C0"] = new PdfLiteral("[" + PdfEncoders.ToString(c0, colorMode) + "]");
            fn.Elements["/C1"] = new PdfLiteral("[" + PdfEncoders.ToString(c1, colorMode) + "]");
            fn.Elements["/N"] = new PdfInteger(1);
            functions.Elements.Add(fn);
        }
        stitching.Elements["/Functions"] = functions;
        return stitching;
    }
}
