using PeachPDF.CSS;
using PeachPDF.Html.Adapters.Entities;

namespace PeachPDF.Html.Core.Utils
{
    /// <summary>
    /// Maps a cascaded <c>text-decoration-style</c> keyword to the <see cref="RDashStyle"/> pen style
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
    /// no <see cref="RDashStyle"/> could express one, so both callers bypass this pen entirely for
    /// <c>wavy</c> and stroke an <see cref="Html.Adapters.RGraphicsPath"/> via
    /// <see cref="WavyDecorationRenderer.StrokeWavyLine"/> instead, the same way they already bypass it
    /// for <c>double</c>.
    /// </summary>
    internal static class TextDecorationStyleMapper
    {
        internal static RDashStyle ToDashStyle(string? style) => style switch
        {
            Keywords.Dotted => RDashStyle.Dot,
            Keywords.Dashed => RDashStyle.Dash,
            _ => RDashStyle.Solid, // solid, and the pen for each stroke of a double; wavy bypasses this pen entirely
        };
    }
}
