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

using PeachPDF.Html.Adapters;
using PeachPDF.Html.Adapters.Entities;
using System.Collections.Generic;

namespace PeachPDF.Svg
{
    /// <summary>
    /// Base class for a paintable node in the SVG scene graph. This is a purely internal model built
    /// once from a parsed SVG document/subtree - it is not a <see cref="Html.Core.Dom.CssBox"/> and
    /// never participates in CSS layout. Properties are mutable only so <see cref="SvgTreeBuilder"/>
    /// can fill in the shared presentation attributes (fill/stroke/opacity/transform/clip) via one
    /// common helper after constructing each element kind's own specific properties; once built, a
    /// document's elements are not mutated again.
    /// </summary>
    internal abstract class SvgElement
    {
        public string? Id { get; set; }

        /// <summary>Default per SVG spec: fill defaults to solid black.</summary>
        public SvgPaint Fill { get; set; } = SvgPaint.Solid(RColor.Black);

        /// <summary>Default per SVG spec: stroke defaults to none.</summary>
        public SvgPaint Stroke { get; set; } = SvgPaint.None;

        public double StrokeWidth { get; set; } = 1;

        /// <summary>Default per SVG spec.</summary>
        public double StrokeMiterLimit { get; set; } = 4;

        /// <summary>Group/element opacity - not inherited (applied as compositing alpha down the subtree), unlike <see cref="FillOpacity"/>/<see cref="StrokeOpacity"/>.</summary>
        public double Opacity { get; set; } = 1;

        /// <summary>Inherited, independent of <see cref="Opacity"/> and <see cref="StrokeOpacity"/>.</summary>
        public double FillOpacity { get; set; } = 1;

        /// <summary>Inherited, independent of <see cref="Opacity"/> and <see cref="FillOpacity"/>.</summary>
        public double StrokeOpacity { get; set; } = 1;

        /// <summary>Inherited. Governs how a self-intersecting fill's interior is determined.</summary>
        public RFillMode FillRule { get; set; } = RFillMode.Nonzero;

        /// <summary>Inherited. Default per SVG spec.</summary>
        public RLineCap StrokeLineCap { get; set; } = RLineCap.Butt;

        /// <summary>Inherited. Default per SVG spec.</summary>
        public RLineJoin StrokeLineJoin { get; set; } = RLineJoin.Miter;

        /// <summary>Inherited. Empty means a solid (non-dashed) stroke.</summary>
        public double[] StrokeDashArray { get; set; } = [];

        /// <summary>Inherited.</summary>
        public double StrokeDashOffset { get; set; }

        public string? ClipPathRef { get; set; }

        /// <summary>Not inherited (like <see cref="ClipPathRef"/>). Id of a <c>&lt;mask&gt;</c> def (see <see cref="SvgDocument.Masks"/>), or null for none.</summary>
        public string? MaskRef { get; set; }

        public RMatrix? Transform { get; set; }

        /// <summary>Inherited. Id of a <c>&lt;marker&gt;</c> def (see <see cref="SvgDocument.Markers"/>), or null for none. Only consulted for shapes markers can attach to - see <see cref="SvgMarkerGeometry"/>.</summary>
        public string? MarkerStartRef { get; set; }

        /// <summary>Inherited. See <see cref="MarkerStartRef"/>.</summary>
        public string? MarkerMidRef { get; set; }

        /// <summary>Inherited. See <see cref="MarkerStartRef"/>.</summary>
        public string? MarkerEndRef { get; set; }
    }

    internal class SvgGroupElement : SvgElement
    {
        public List<SvgElement> Children { get; } = [];
    }

    /// <summary>
    /// An <c>&lt;a&gt;</c> element - groups children exactly like <c>&lt;g&gt;</c>, but also carries a
    /// link target. Rendering it additionally reports a PDF link annotation covering the (axis-aligned
    /// bounding box of the) transformed rendered area - see <see cref="SvgRenderer"/>. An <c>&lt;a&gt;</c>
    /// with no <c>href</c> still renders its children normally, just without becoming a link (per spec).
    /// </summary>
    internal sealed class SvgAnchorElement : SvgGroupElement
    {
        public string? Href { get; set; }
    }

    internal sealed class SvgPathElement : SvgElement
    {
        public IReadOnlyList<PathSegment> Segments { get; set; } = [];
    }

    internal enum SvgTextAnchor { Start, Middle, End }

    /// <summary>
    /// A single positioned text run - built from a <c>&lt;text&gt;</c>, <c>&lt;tspan&gt;</c>, or
    /// <c>&lt;tref&gt;</c> element (all three share this shape; only the root of a subtree is ever an
    /// actual <c>&lt;text&gt;</c>). <see cref="HasOwnX"/>/<see cref="HasOwnY"/> distinguish "this run
    /// starts a new absolute position" from "this run continues immediately after its previous
    /// sibling's rendered width" (ordinary SVG text flow) - resolved at render time, once each run's
    /// measured width is known (see <see cref="SvgRenderer"/>). Per-character <c>x</c>/<c>y</c>/
    /// <c>dx</c>/<c>dy</c>/<c>rotate</c> arrays (SVG 1.1's full per-glyph positioning model) are out of
    /// scope in v1 - only a single leading value applies to the whole run, the same simplification
    /// already used elsewhere in this renderer (e.g. <c>&lt;switch&gt;</c>'s first-child-only rule).
    /// A solid <see cref="SvgElement.Fill"/> is painted with the fast <see cref="Html.Adapters.RGraphics.DrawString"/>
    /// path (a single-color text show, kept selectable); a gradient/pattern fill or any
    /// <see cref="SvgElement.Stroke"/> instead outlines the glyph run to a vector path
    /// (<see cref="Html.Adapters.RGraphics.GetTextOutline"/>) and fills/strokes it through the same
    /// brush/pen machinery shapes use. When <see cref="PathData"/> is set (a <c>&lt;textPath&gt;</c>),
    /// the run's glyphs are laid out along that path instead of a straight baseline.
    /// </summary>
    internal sealed class SvgTextElement : SvgElement
    {
        public bool HasOwnX { get; set; }
        public bool HasOwnY { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double Dx { get; set; }
        public double Dy { get; set; }
        public double RotateDegrees { get; set; }
        public SvgTextAnchor TextAnchor { get; set; } = SvgTextAnchor.Start;

        /// <summary>Null when the resolved font family (and the library-wide default fallback) couldn't be found - the run then renders nothing rather than throwing, unlike ordinary HTML text.</summary>
        public RFont? Font { get; set; }

        /// <summary>This run's own direct text content only - not its <see cref="Spans"/>' text.</summary>
        public string Text { get; set; } = "";

        /// <summary>Child <c>&lt;tspan&gt;</c>/<c>&lt;tref&gt;</c> runs, in document order.</summary>
        public List<SvgTextElement> Spans { get; } = [];

        /// <summary>
        /// The referenced path's geometry when this run is a <c>&lt;textPath&gt;</c> (its <c>href</c>
        /// resolved to a <c>&lt;path&gt;</c>'s <c>d</c>), else null. When set, the run's glyphs are
        /// placed along the path via arc-length positioning rather than on a straight baseline.
        /// </summary>
        public IReadOnlyList<PathSegment>? PathData { get; set; }

        /// <summary>The <c>startOffset</c> distance along the path where the run begins (default 0).</summary>
        public double StartOffset { get; set; }

        /// <summary>Whether <see cref="StartOffset"/> is a percentage of the path's total length (else a user-space length).</summary>
        public bool StartOffsetIsPercent { get; set; }
    }

    internal sealed class SvgCircleElement : SvgElement
    {
        public double Cx { get; set; }
        public double Cy { get; set; }
        public double R { get; set; }
    }

    internal sealed class SvgPolygonElement : SvgElement
    {
        public RPoint[] Points { get; set; } = [];
    }

    /// <summary>
    /// <c>&lt;polyline&gt;</c> - geometrically identical to <see cref="SvgPolygonElement"/> except its
    /// stroke never draws a closing segment back to the first point. Per spec, fill still behaves as
    /// if the shape were closed; this implementation deliberately simplifies that by using the same
    /// (unclosed) geometry for both fill and stroke - a documented v1 gap that only affects the rare
    /// case of a filled (rather than the far more common <c>fill="none"</c>) polyline.
    /// </summary>
    internal sealed class SvgPolylineElement : SvgElement
    {
        public RPoint[] Points { get; set; } = [];
    }

    internal sealed class SvgRectElement : SvgElement
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }

        /// <summary>Corner radii, already defaulted (each to the other, per spec) and clamped to half the rect's width/height.</summary>
        public double Rx { get; set; }
        public double Ry { get; set; }
    }

    internal sealed class SvgEllipseElement : SvgElement
    {
        public double Cx { get; set; }
        public double Cy { get; set; }
        public double Rx { get; set; }
        public double Ry { get; set; }
    }

    /// <summary>An open, unclosed line segment - fill has no visible effect (zero area).</summary>
    internal sealed class SvgLineElement : SvgElement
    {
        public double X1 { get; set; }
        public double Y1 { get; set; }
        public double X2 { get; set; }
        public double Y2 { get; set; }
    }

    /// <summary>
    /// An <c>&lt;image&gt;</c> element, resolved into either <see cref="Image"/> for a raster payload
    /// or <see cref="NestedDocument"/> for an embedded <c>image/svg+xml</c> payload (mutually
    /// exclusive). A <c>data:</c> URI <c>href</c> is decoded in-memory during the synchronous
    /// <see cref="SvgTreeBuilder.Build"/>; a network URL or file-path href is fetched ahead of the
    /// build by <see cref="SvgTreeBuilder.PrefetchImageResourcesAsync"/> (the same async pipeline HTML
    /// <c>&lt;img&gt;</c> uses) and handed to the builder as a resolved map. An unresolvable href (no
    /// configured loader, missing resource, malformed payload) leaves both properties null and the
    /// element simply renders nothing.
    /// </summary>
    internal sealed class SvgImageElement : SvgElement
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public SvgPreserveAspectRatio PreserveAspectRatio { get; set; } = SvgPreserveAspectRatio.Default;
        public RImage? Image { get; set; }
        public SvgDocument? NestedDocument { get; set; }
    }

    /// <summary>
    /// A resolved <c>&lt;use&gt;</c> reference - <see cref="Target"/> holds the referenced element for
    /// the renderer to paint at this node's own position/attributes. <see cref="Width"/>/<see cref="Height"/>
    /// only have an effect when <see cref="Target"/> is a <see cref="SvgSymbolElement"/> or
    /// <see cref="SvgNestedSvgElement"/> (per spec, they're ignored for any other reference target).
    /// </summary>
    internal sealed class SvgUseElement : SvgElement
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double? Width { get; set; }
        public double? Height { get; set; }
        public SvgElement? Target { get; set; }
    }

    /// <summary>
    /// A nested <c>&lt;svg&gt;</c> - establishes a new viewport/coordinate system at (<see cref="X"/>,
    /// <see cref="Y"/>) sized <see cref="Width"/>x<see cref="Height"/> (already defaulted to the
    /// enclosing viewport's size if not specified, per spec's 100% default).
    /// </summary>
    internal sealed class SvgNestedSvgElement : SvgElement
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public RRect? ViewBox { get; set; }
        public SvgPreserveAspectRatio PreserveAspectRatio { get; set; } = SvgPreserveAspectRatio.Default;
        public List<SvgElement> Children { get; } = [];
    }

    /// <summary>
    /// A <c>&lt;symbol&gt;</c> definition - never painted directly (like <c>&lt;defs&gt;</c> content),
    /// only through a <see cref="SvgUseElement"/> reference, which establishes the actual viewport
    /// (a symbol has no size of its own - unlike <see cref="SvgNestedSvgElement"/>, it gets sized
    /// entirely by the referencing <c>&lt;use&gt;</c>, defaulting to the current viewport's size).
    /// </summary>
    internal sealed class SvgSymbolElement : SvgElement
    {
        public RRect? ViewBox { get; set; }
        public SvgPreserveAspectRatio PreserveAspectRatio { get; set; } = SvgPreserveAspectRatio.Default;
        public List<SvgElement> Children { get; } = [];
    }

    /// <summary>
    /// A <c>&lt;marker&gt;</c> definition - never painted directly (like <c>&lt;defs&gt;</c> content),
    /// only through a shape's <c>marker-start</c>/<c>marker-mid</c>/<c>marker-end</c> reference (see
    /// <see cref="SvgDocument.Markers"/>). Not an <see cref="SvgElement"/> itself, matching
    /// <see cref="SvgGradient"/>/<see cref="SvgClipPath"/>'s precedent for pure definitions.
    /// </summary>
    internal sealed class SvgMarkerElement
    {
        public double RefX { get; set; }
        public double RefY { get; set; }

        /// <summary>Defaults per spec.</summary>
        public double MarkerWidth { get; set; } = 3;
        public double MarkerHeight { get; set; } = 3;

        public RRect? ViewBox { get; set; }
        public SvgPreserveAspectRatio PreserveAspectRatio { get; set; } = SvgPreserveAspectRatio.Default;

        /// <summary>True for <c>orient="auto"</c> or <c>"auto-start-reverse"</c> - rotate to match the attachment vertex's tangent direction, rather than a fixed <see cref="OrientAngle"/>.</summary>
        public bool OrientAuto { get; set; }

        /// <summary>True only for <c>orient="auto-start-reverse"</c> - like <see cref="OrientAuto"/>, but a marker placed at a shape's start vertex is additionally rotated 180 degrees.</summary>
        public bool OrientAutoStartReverse { get; set; }

        /// <summary>Fixed rotation in degrees, used when neither <see cref="OrientAuto"/> nor <see cref="OrientAutoStartReverse"/> is set.</summary>
        public double OrientAngle { get; set; }

        /// <summary>True (the spec default) for <c>markerUnits="strokeWidth"</c> - scale the marker by the host shape's stroke-width; false for <c>"userSpaceOnUse"</c> (no scaling).</summary>
        public bool MarkerUnitsStrokeWidth { get; set; } = true;

        public List<SvgElement> Children { get; } = [];
    }
}
