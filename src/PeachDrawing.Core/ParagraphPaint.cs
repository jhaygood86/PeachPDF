using System;

namespace PeachDrawing.Core
{
    /// <summary>The lines drawn along a run of text.</summary>
    [Flags]
    public enum TextDecorations
    {
        /// <summary>No decoration.</summary>
        None = 0,

        /// <summary>A line under the text, at the position and thickness the font gives.</summary>
        Underline = 1,

        /// <summary>A line above the text, at the top of the ascent.</summary>
        Overline = 2,

        /// <summary>A line through the middle of the text, at the position and thickness the font gives.</summary>
        LineThrough = 4,
    }

    /// <summary>How one run of a paragraph is painted: what a layout, which knows only about faces, sizes and positions, leaves to the caller.</summary>
    /// <param name="Color">the color of the text</param>
    /// <param name="Decorations">the lines drawn along the run</param>
    /// <param name="DecorationColor">the color of those lines, or <see langword="null"/> for <paramref name="Color"/></param>
    public readonly record struct ParagraphPaint(PaintColor Color, TextDecorations Decorations = TextDecorations.None, PaintColor? DecorationColor = null);
}
