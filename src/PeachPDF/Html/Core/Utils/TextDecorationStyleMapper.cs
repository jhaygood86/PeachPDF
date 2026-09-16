using PeachPDF.CSS;
using PeachPDF.Html.Adapters.Entities;

namespace PeachPDF.Html.Core.Utils
{
    /// <summary>
    /// Maps a cascaded <c>text-decoration-style</c> keyword to the <see cref="RDashStyle"/> pen style
    /// used to paint it - shared by <see cref="Paint.FragmentPainter"/> (HTML) and
    /// <see cref="Svg.SvgRenderer"/> (SVG) so the one simplification this maps in (<c>wavy</c> has no
    /// <see cref="RDashStyle"/> that could express it, so it paints solid) lives in one place rather
    /// than two independently-maintained copies.
    ///
    /// <c>double</c> also resolves to a solid pen here, but is not a simplification: it is two solid
    /// strokes rather than one patterned stroke, so the second line is drawn by
    /// <c>FragmentPainter.StrokeDecorationSegment</c> and the pen this returns is the one each of them
    /// uses. SVG's decorations do not yet make that distinction and still draw a single line.
    /// </summary>
    internal static class TextDecorationStyleMapper
    {
        internal static RDashStyle ToDashStyle(string? style) => style switch
        {
            Keywords.Dotted => RDashStyle.Dot,
            Keywords.Dashed => RDashStyle.Dash,
            _ => RDashStyle.Solid, // solid, and the pen for each stroke of a double; wavy is the simplification
        };
    }
}
