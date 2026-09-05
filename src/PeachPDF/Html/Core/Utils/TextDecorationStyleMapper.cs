using PeachPDF.CSS;
using PeachPDF.Html.Adapters.Entities;

namespace PeachPDF.Html.Core.Utils
{
    /// <summary>
    /// Maps a cascaded <c>text-decoration-style</c> keyword to the <see cref="RDashStyle"/> pen style
    /// used to paint it - shared by <see cref="Paint.FragmentPainter"/> (HTML) and
    /// <see cref="Svg.SvgRenderer"/> (SVG) so the one simplification this maps in (<c>double</c>/
    /// <c>wavy</c> have no dedicated <see cref="RDashStyle"/> yet, so both paint solid) lives in one
    /// place rather than two independently-maintained copies.
    /// </summary>
    internal static class TextDecorationStyleMapper
    {
        internal static RDashStyle ToDashStyle(string? style) => style switch
        {
            Keywords.Dotted => RDashStyle.Dot,
            Keywords.Dashed => RDashStyle.Dash,
            _ => RDashStyle.Solid, // solid/double/wavy - no dedicated RDashStyle for double/wavy yet
        };
    }
}
