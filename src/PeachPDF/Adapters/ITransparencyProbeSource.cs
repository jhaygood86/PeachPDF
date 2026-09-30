namespace PeachPDF.Adapters;

/// <summary>
/// Implemented by a <see cref="PeachDrawing.Core.Canvas"/> that can answer whether painting something would need transparency
/// (a PDF graphics, for PDF/A and PDF/X output that forbids it). A graphics with no notion of transparency - a raster surface, or a
/// third party's canvas - simply does not implement it, and the painter treats it as needing none.
/// </summary>
internal interface ITransparencyProbeSource
{
    /// <summary>A probe that renders a paint callback into a scratch graphics and reports whether it used transparency.</summary>
    TransparencyProbe CreateTransparencyProbe();
}
