using PeachDrawing.Core;
using PeachDrawing;
using PeachDrawing.Filters;
using System;

namespace PeachBrackets.Svg;

/// <summary>
/// The two <c>PeachDrawing.Filters.FilterOps</c> pixel operations that stay PeachPDF-side rather than
/// moving with the rest of that class: both take an SVG filter-primitive descriptor
/// (<see cref="TransferFunction"/>, <see cref="FeConvolveMatrix"/>) straight from SVG parsing as a
/// parameter, which is genuinely SVG-specific - <c>PeachDrawing</c> has no SVG concept at all - unlike
/// every other <c>FilterOps</c> method, which takes only <c>RasterSurface</c>s and plain values and moved
/// with the rest of the raster backend. See <c>PeachDrawing.Filters.FilterOps</c>'s own remarks.
/// </summary>
internal static class SvgFilterOps
{
    /// <summary>Evaluates one transfer function over the 256 possible 8-bit inputs.</summary>
    public static byte[] BuildTransferLut(TransferFunction function) =>
        ComponentTransferEffect.BuildTable((TransferFunctionKind)(int)function.Kind, function.Table, function.Slope, function.Intercept, function.Amplitude, function.Exponent, function.Offset);

    public static void ConvolveMatrix(RasterSurface source, RasterSurface destination, FeConvolveMatrix p) =>
        FilterOps.ConvolveMatrix(source, destination, new ConvolveMatrixEffect(p.OrderX, p.OrderY, p.Kernel, p.Divisor, p.Bias, p.TargetX, p.TargetY, p.EdgeMode, p.PreserveAlpha));
}
