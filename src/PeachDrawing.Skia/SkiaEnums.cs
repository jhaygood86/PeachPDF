namespace PeachDrawing.Skia;

/// <summary>Which engine measures, shapes and draws the text of a <see cref="SkiaRenderContext"/>'s fonts.</summary>
public enum SkiaTextEngine
{
    /// <summary>
    /// The shared <c>PeachDrawing.Text</c> engine, which does the full text shaping itself (GSUB/GPOS, joining scripts, Indic shaping, bidi): text
    /// is shaped from the font file and drawn as glyph outlines, so a run measures and lays out exactly as it does on the PDF, SVG and raster
    /// canvases, and no shaping library is needed.
    /// </summary>
    PeachDrawing,

    /// <summary>
    /// Skia's own fonts: families are resolved by the platform's font manager, shaped by the context's <see cref="ISkiaTextShaper"/> (HarfBuzz, if the host supplies it) and drawn by Skia as glyph runs, so the
    /// text looks like the rest of the platform's (and, on a GPU surface, is drawn from the glyph atlas) but does not lay out identically
    /// to the other canvases.
    /// </summary>
    Native,
}

/// <summary>Which decoder turns an encoded image (PNG, JPEG, ...) into the pixels a <see cref="SkiaRenderContext"/> draws.</summary>
public enum SkiaImageCodec
{
    /// <summary>The shared PeachImage decoder: the same formats and the same pixels as on every other PeachDrawing canvas.</summary>
    PeachImage,

    /// <summary>
    /// Skia's own codecs, which the platform may accelerate and which keep the decoded image in a form a GPU surface can upload once. A
    /// format Skia cannot decode is handed to PeachImage instead unless <see cref="SkiaRenderContext.FallbackToPeachImage"/> is switched off.
    /// </summary>
    Native,
}

/// <summary>The file format <see cref="SkiaComposer.Save"/> writes.</summary>
public enum SkiaImageFormat
{
    /// <summary>PNG, lossless and with transparency.</summary>
    Png,

    /// <summary>JPEG, lossy and without transparency.</summary>
    Jpeg,

    /// <summary>WebP.</summary>
    Webp,
}
