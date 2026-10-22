namespace PeachDrawing.Svg;

/// <summary>How an <see cref="SvgCanvas"/> writes text.</summary>
public enum SvgTextMode
{
    /// <summary>
    /// Every glyph is written as a vector outline (<c>&lt;path&gt;</c>). The text looks identical in every viewer, whatever fonts it has,
    /// and shaping, kerning, synthetic bold and italic, colour glyphs and SVG glyphs are all exact; the text is not selectable.
    /// </summary>
    Outlines = 0,

    /// <summary>
    /// Text is written as selectable <c>&lt;text&gt;</c> with each character placed at the position the shaper chose, in a subset of the
    /// font embedded in the document as an <c>@font-face</c>. A run the viewer could not reproduce that way - ligatures, complex scripts,
    /// colour or bitmap glyphs, a font that cannot be embedded as OpenType - is still written as outlines.
    /// </summary>
    Text = 1,
}
