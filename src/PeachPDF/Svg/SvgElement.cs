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

        public RMatrix? Transform { get; set; }
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
}
