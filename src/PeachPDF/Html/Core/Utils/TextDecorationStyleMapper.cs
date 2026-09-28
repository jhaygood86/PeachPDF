using PeachPDF.CSS;
using PeachDrawing.Abstractions;

namespace PeachPDF.Html.Core.Utils
{
    /// <summary>
    /// Maps a cascaded <c>text-decoration-style</c> keyword to the <see cref="DashStyle"/> pen style
    /// used to paint it - shared by <see cref="Paint.FragmentPainter"/> (HTML) and
    /// <see cref="Svg.SvgRenderer"/> (SVG) so the two keywords that are not simply a dash pattern
    /// (<c>double</c>, <c>wavy</c>) still only need to be recognized in one place.
    ///
    /// <c>double</c> resolves to a solid pen here: it is two solid strokes rather than one patterned
    /// stroke, so the second line is drawn by <c>FragmentPainter.StrokeDecorationSegment</c> and the pen
    /// this returns is the one each of them uses. SVG's decorations do not yet make that distinction and
    /// still draw a single line.
    ///
    /// <c>wavy</c> also resolves to a solid pen here, but the pen is never used to stroke a wave with -
    /// no <see cref="DashStyle"/> could express one, so both callers bypass this pen entirely for
    /// <c>wavy</c> and stroke an <see cref="GraphicsPath"/> via
    /// <see cref="WavyDecorationRenderer.StrokeWavyLine"/> instead, the same way they already bypass it
    /// for <c>double</c>.
    /// </summary>
    internal static class TextDecorationStyleMapper
    {
        internal static DashStyle ToDashStyle(TextDecorationStyleMode style) => style switch
        {
            TextDecorationStyleMode.Dotted => DashStyle.Dot,
            TextDecorationStyleMode.Dashed => DashStyle.Dash,
            _ => DashStyle.Solid, // solid, and the pen for each stroke of a double; wavy bypasses this pen entirely
        };

        /// <summary>The same, for the keyword text of an SVG <c>text-decoration-style</c> attribute; an unrecognized keyword is <c>solid</c>.</summary>
        internal static DashStyle ToDashStyle(string? style) =>
            style is not null && Map.TextDecorationStyleModes.TryGetValue(style, out var mode) ? ToDashStyle(mode) : DashStyle.Solid;
    }
}
