namespace PeachDrawing.CoreGraphics;

/// <summary>Which engine measures, shapes and draws the text of a <see cref="CoreGraphicsRenderContext"/>'s fonts.</summary>
public enum CoreGraphicsTextEngine
{
    /// <summary>
    /// The default. The shared <c>PeachDrawing.Text</c> engine, which does the full text shaping itself (GSUB/GPOS, joining scripts, Indic shaping, bidi): text
    /// is shaped from the font file and drawn as glyph outlines, so a run measures and lays out exactly as it does on the PDF, SVG and raster canvases.
    /// </summary>
    PeachDrawing,

    /// <summary>
    /// CoreText: families are resolved by the system's font manager, shaped by CoreText and drawn as glyph runs, so the text looks like the rest of the
    /// platform's (system font, optical sizes, font smoothing) but does not lay out identically to the other canvases.
    /// </summary>
    Native,
}

/// <summary>Which decoder turns an encoded image (PNG, JPEG, ...) into the pixels a <see cref="CoreGraphicsRenderContext"/> draws.</summary>
public enum CoreGraphicsImageCodec
{
    /// <summary>The default. The shared PeachImage decoder: the same formats and the same pixels as on every other PeachDrawing canvas.</summary>
    PeachImage,

    /// <summary>
    /// ImageIO, the codecs the operating system ships, which the hardware may accelerate. A format ImageIO cannot decode is handed to PeachImage
    /// instead unless <see cref="CoreGraphicsRenderContext.FallbackToPeachImage"/> is switched off.
    /// </summary>
    Native,

    /// <summary>
    /// Chosen image by image. When the context has a Metal device (so the system can decode in hardware and the result stays in GPU-friendly memory),
    /// an image ImageIO has a codec for is kept as its encoded bytes and decoded by the system on first draw, so no managed copy of its pixels is made
    /// unless something asks for them; any other image, any image with an orientation stored in its metadata, and every image on a context without a
    /// Metal device is decoded by PeachImage. Pixels can differ slightly from PeachImage's (colour-managed and CMYK files are converted by the system).
    /// </summary>
    Auto,
}

/// <summary>The file format <see cref="CoreGraphicsComposer.Save"/> writes.</summary>
public enum CoreGraphicsImageFormat
{
    /// <summary>PNG, lossless and with transparency.</summary>
    Png,

    /// <summary>JPEG, lossy and without transparency.</summary>
    Jpeg,

    /// <summary>TIFF, lossless and with transparency.</summary>
    Tiff,
}

/// <summary>How glyphs are positioned on the device's pixel grid; see <see cref="CoreGraphicsRenderContext.GlyphPlacement"/>.</summary>
public enum CoreGraphicsGlyphPlacement
{
    /// <summary>
    /// As Apple's own text is placed. The baseline of a line is rounded to a whole device pixel, and each glyph's horizontal origin is rounded down to a fraction of one: a half pixel for text
    /// 17 to 33 device pixels high, a third for 12 to 16, a quarter for 9 to 11 and a fifth below that (on macOS; iOS stops at a third), and a whole pixel from 34 up (the grid is 1/n pixel, with n the least whole number such
    /// that n times the size reaches 33.3, capped per platform). Text then has crisp baselines, and the same letter looks the same wherever it falls. The default.
    /// </summary>
    Platform,

    /// <summary>Every glyph at exactly the position layout gave it, in both directions. Text is laid out as measured, at the cost of a soft baseline at a fractional position.</summary>
    Exact,
}
