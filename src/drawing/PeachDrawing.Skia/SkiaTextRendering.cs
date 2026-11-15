namespace PeachDrawing.Skia;

/// <summary>
/// How a <see cref="SkiaRenderContext"/> with the <see cref="SkiaTextEngine.PeachDrawing"/> engine turns a run of text into pixels. It does
/// not apply to <see cref="SkiaTextEngine.Native"/> text, which Skia rasterizes itself.
/// </summary>
public enum SkiaTextRendering
{
    /// <summary>
    /// Chooses for each run, so text looks like the rest of the user's desktop wherever that is possible and stays crisp vector text where it is not.
    /// Large text, and text that is rotated, skewed or scaled unevenly, is drawn as <see cref="Outline"/>. Other text is drawn as <see cref="Lcd"/>
    /// when the canvas draws on screen over an opaque backdrop (see <see cref="SkiaCanvas.OpaqueBackdrop"/>) and the operating system's own
    /// setting asks for subpixel text: ClearType on Windows, the desktop's font smoothing (the <c>rgba</c> setting) on Linux where it can be read.
    /// macOS never asks for it (Apple removed subpixel text), so text there is <see cref="Grayscale"/>. Anywhere else it is drawn as
    /// <see cref="Grayscale"/>. A <see cref="SkiaComposer"/>'s surface is an export that may be shown on any display, so it never gets
    /// color fringes by default: ask for <see cref="Lcd"/> there.
    /// </summary>
    Auto = 0,

    /// <summary>
    /// Each glyph's exact outline is filled as a vector shape by Skia. Text keeps its full vector quality under any transform and can be drawn with
    /// any brush, but it is not fitted to the pixel grid and its coverage is blended without any correction for how the eye sees light and
    /// dark text, so small text looks lighter and softer than the operating system draws it.
    /// </summary>
    Outline = 1,

    /// <summary>
    /// Text is fitted to the pixel grid by the font's own hinting, rasterized to a coverage mask, corrected for the brightness of the text
    /// color so dark and light text have the same apparent weight, and drawn as an image placed on whole device pixels. A run that is rotated,
    /// skewed or scaled unevenly, or in a color font, is drawn as <see cref="Outline"/>.
    /// </summary>
    Grayscale = 2,

    /// <summary>
    /// As <see cref="Grayscale"/>, but with each pixel's red, green and blue given their own coverage from the three stripes of an LCD
    /// pixel (ClearType-style), which triples the horizontal detail. Blending each channel assumes what is behind the text is opaque, so it
    /// applies only to a canvas whose <see cref="SkiaCanvas.OpaqueBackdrop"/> is set, outside a translucent layer and with the ordinary blend;
    /// elsewhere the text is drawn as <see cref="Grayscale"/>.
    /// </summary>
    Lcd = 3,
}
