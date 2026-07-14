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
        PatternRef,
    }

    /// <summary>
    /// A resolved SVG paint value (<c>fill</c>/<c>stroke</c>): either no paint, a solid color, or a
    /// reference (by id) to a gradient or pattern defined elsewhere in the document. <c>url(#id)</c>
    /// paint values are always initially parsed as <see cref="SvgPaintKind.GradientRef"/> (see
    /// <see cref="SvgValueParsers.ParsePaint"/>, which has no document context to check against) - a
    /// reference that actually turns out to name a <c>&lt;pattern&gt;</c> gets reclassified to
    /// <see cref="SvgPaintKind.PatternRef"/> once the id registry is available (see
    /// <see cref="SvgTreeBuilder"/>).
    /// </summary>
    internal readonly struct SvgPaint
    {
        public SvgPaintKind Kind { get; private init; }
        public RColor Color { get; private init; }
        public string? ReferenceId { get; private init; }

        public static readonly SvgPaint None = new() { Kind = SvgPaintKind.None };

        public static SvgPaint Solid(RColor color) => new() { Kind = SvgPaintKind.Solid, Color = color };

        public static SvgPaint GradientRef(string id) => new() { Kind = SvgPaintKind.GradientRef, ReferenceId = id };

        public static SvgPaint PatternRef(string id) => new() { Kind = SvgPaintKind.PatternRef, ReferenceId = id };
    }
}
