namespace PeachDrawing.Win2D;

/// <summary>Which engine measures, shapes and draws the text of a <see cref="Win2dRenderContext"/>'s fonts.</summary>
public enum Win2dTextEngine
{
    /// <summary>
    /// The shared <c>PeachDrawing.Text</c> engine: text is shaped from the font file and drawn as glyph outlines, so a run measures and
    /// lays out exactly as it does on the PDF, SVG and raster canvases.
    /// </summary>
    PeachDrawing,

    /// <summary>
    /// DirectWrite, through Win2D's text layouts: installed fonts are resolved, shaped, hinted and drawn by Windows, so the text looks like
    /// the rest of the system's but does not lay out identically to the other canvases.
    /// </summary>
    DirectWrite,
}
