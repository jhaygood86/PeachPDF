namespace PeachDrawing.Win2D;

/// <summary>
/// How a <see cref="Win2dRenderContext"/> with the <see cref="Win2dTextEngine.PeachDrawing"/> engine turns a run of text into pixels. It does
/// not apply to <see cref="Win2dTextEngine.DirectWrite"/> text, which Windows rasterizes itself.
/// </summary>
public enum Win2dTextRendering
{
    /// <summary>
    /// Chooses for each run, so text looks like the rest of Windows wherever that is possible and stays crisp vector text where it is not.
    /// Large text, and text that is rotated or skewed, is drawn as <see cref="Outline"/>. Other text is drawn as <see cref="Lcd"/> when the
    /// canvas draws on screen (over a drawing session the host owns, such as a control's) onto an opaque surface whose pixels can be read back,
    /// and Windows' ClearType is switched on; otherwise as <see cref="Grayscale"/>. A <see cref="Win2dComposer"/>'s bitmap is an export that
    /// may be shown on any display, so it never gets colour fringes by default: ask for <see cref="Lcd"/> there.
    /// </summary>
    Auto = 0,

    /// <summary>
    /// Each glyph's exact outline is filled as a vector shape. Text keeps its full vector quality under any transform and can be drawn with
    /// any brush, but it is not fitted to the pixel grid and its coverage is blended without any correction for how the eye sees light and
    /// dark text, so small text looks lighter and softer than Windows draws it.
    /// </summary>
    Outline = 1,

    /// <summary>
    /// Text is fitted to the pixel grid by the font's own hinting, rasterized to a coverage mask, corrected for the brightness of the text
    /// colour so dark and light text have the same apparent weight, and drawn as a bitmap placed on whole device pixels. Close to how Windows
    /// draws grayscale text. A run that is rotated or skewed, or in a colour font, is drawn as <see cref="Outline"/>.
    /// </summary>
    Grayscale = 2,

    /// <summary>
    /// As <see cref="Grayscale"/>, but with each pixel's red, green and blue given their own coverage from the three stripes of an LCD
    /// pixel (ClearType-style), which triples the horizontal detail. Blending each channel needs the pixels behind the text, so it applies
    /// only where the canvas can read them back (a <see cref="Win2dComposer"/>'s own bitmap, or a drawing session over an opaque bitmap, with the
    /// ordinary blend); elsewhere the text is drawn as <see cref="Grayscale"/>.
    /// </summary>
    Lcd = 3,
}
