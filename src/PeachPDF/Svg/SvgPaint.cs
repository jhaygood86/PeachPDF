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

using PeachDrawing.Core;

namespace PeachPDF.Svg
{
    internal enum SvgPaintKind
    {
        None,
        Solid,
        GradientRef,
        PatternRef,

        /// <summary>
        /// The <c>context-fill</c> keyword, not yet resolved: SVG 2 (painting, "context paint") makes it the fill of the context element,
        /// which is a <c>use</c> for what it instantiates, the shape a marker is drawn on for the marker's content, and for a glyph
        /// document the text. The tree builder resolves it for content it can (through <c>use</c> and from the seed given to the build);
        /// what remains, inside a marker, is resolved by the renderer against the shape being marked.
        /// </summary>
        ContextFill,

        /// <summary>The <c>context-stroke</c> keyword; see <see cref="ContextFill"/>.</summary>
        ContextStroke,
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
        public PaintColor PaintColor { get; private init; }
        public string? ReferenceId { get; private init; }

        /// <summary>
        /// The element whose bounding box a gradient or pattern that came through <c>context-fill</c>/<c>context-stroke</c> is measured
        /// against (its <c>objectBoundingBox</c> units): the context element, not the element painted. Null for a paint that was written
        /// on the element itself.
        /// </summary>
        public SvgElement? ContextElement { get; private init; }

        public static readonly SvgPaint None = new() { Kind = SvgPaintKind.None };

        public static SvgPaint Solid(PaintColor color) => new() { Kind = SvgPaintKind.Solid, PaintColor = color };

        public static SvgPaint GradientRef(string id) => new() { Kind = SvgPaintKind.GradientRef, ReferenceId = id };

        public static SvgPaint PatternRef(string id) => new() { Kind = SvgPaintKind.PatternRef, ReferenceId = id };

        /// <summary>The unresolved <c>context-fill</c> keyword.</summary>
        public static readonly SvgPaint ContextFill = new() { Kind = SvgPaintKind.ContextFill };

        /// <summary>The unresolved <c>context-stroke</c> keyword.</summary>
        public static readonly SvgPaint ContextStroke = new() { Kind = SvgPaintKind.ContextStroke };

        /// <summary>
        /// This paint as the paint of <paramref name="contextElement"/>, for a paint that will be used by content inside it: a gradient or
        /// pattern then measures its <c>objectBoundingBox</c> against that element. A colour or <c>none</c> is returned as it is, and so is
        /// a paint that already names a context element (the nearest one wins).
        /// </summary>
        public SvgPaint OfContextElement(SvgElement contextElement) =>
            Kind is SvgPaintKind.GradientRef or SvgPaintKind.PatternRef && ContextElement is null ? this with { ContextElement = contextElement } : this;
    }
}
