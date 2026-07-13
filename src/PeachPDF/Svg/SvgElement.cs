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

        public double Opacity { get; set; } = 1;

        public string? ClipPathRef { get; set; }

        public RMatrix? Transform { get; set; }
    }

    internal sealed class SvgGroupElement : SvgElement
    {
        public List<SvgElement> Children { get; } = [];
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
    /// A resolved <c>&lt;use&gt;</c> reference - <see cref="Target"/> holds the referenced element for
    /// the renderer to paint at this node's own position/attributes.
    /// </summary>
    internal sealed class SvgUseElement : SvgElement
    {
        public double X { get; set; }
        public double Y { get; set; }
        public SvgElement? Target { get; set; }
    }
}
