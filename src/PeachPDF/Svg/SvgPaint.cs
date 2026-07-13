// "Therefore those skilled at the unorthodox
// are infinite as heaven and earth,
// inexhaustible as the great rivers.
// When they come to an end,
// they begin again,
// like the days and months;
// they die and are reborn,
// like the four seasons."
//
// - Sun Tsu,
// "The Art of War"

using PeachPDF.Html.Adapters.Entities;

namespace PeachPDF.Svg
{
    internal enum SvgPaintKind
    {
        None,
        Solid,
        GradientRef,
    }

    /// <summary>
    /// A resolved SVG paint value (<c>fill</c>/<c>stroke</c>): either no paint, a solid color, or a
    /// reference to a gradient defined elsewhere in the document (by id).
    /// </summary>
    internal readonly struct SvgPaint
    {
        public SvgPaintKind Kind { get; private init; }
        public RColor Color { get; private init; }
        public string? GradientId { get; private init; }

        public static readonly SvgPaint None = new() { Kind = SvgPaintKind.None };

        public static SvgPaint Solid(RColor color) => new() { Kind = SvgPaintKind.Solid, Color = color };

        public static SvgPaint GradientRef(string id) => new() { Kind = SvgPaintKind.GradientRef, GradientId = id };
    }
}
