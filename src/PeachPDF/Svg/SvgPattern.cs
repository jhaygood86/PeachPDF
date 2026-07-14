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
    /// A <c>&lt;pattern&gt;</c> definition - never painted directly, only referenced by a shape's
    /// <c>fill</c>/<c>stroke</c> via <see cref="SvgPaint.PatternRef"/>. Lives only in
    /// <see cref="SvgDocument"/>'s pattern registry, matching <see cref="SvgGradient"/>'s precedent
    /// for a pure (non-<see cref="SvgElement"/>) definition.
    /// </summary>
    internal sealed class SvgPattern
    {
        public string? Id { get; init; }

        /// <summary>Tile position/size. Interpreted per <see cref="PatternUnitsUserSpaceOnUse"/> - resolved against the referencing shape's bounding box at paint time when false (the spec default), same as <see cref="SvgGradient"/>'s objectBoundingBox handling.</summary>
        public double X { get; init; }
        public double Y { get; init; }
        public double Width { get; init; }
        public double Height { get; init; }

        /// <summary>False (the spec default) means <c>objectBoundingBox</c> - X/Y/Width/Height are fractions of the referencing shape's bounding box.</summary>
        public bool PatternUnitsUserSpaceOnUse { get; init; }

        /// <summary>
        /// True (the spec default for THIS attribute, unlike <see cref="PatternUnitsUserSpaceOnUse"/>)
        /// means the pattern's own <see cref="Children"/> are drawn in ordinary user-space units; false
        /// (<c>patternContentUnits="objectBoundingBox"</c>) means the pattern's content coordinate
        /// system is itself scaled to the referencing shape's bounding box - a rare combination, not
        /// resolved by this v1 implementation (content is always drawn as literal user-space units,
        /// same as if this were true) - a documented simplification.
        /// </summary>
        public bool PatternContentUnitsUserSpaceOnUse { get; init; } = true;

        public RMatrix? PatternTransform { get; init; }

        public RRect? ViewBox { get; init; }
        public SvgPreserveAspectRatio PreserveAspectRatio { get; init; } = SvgPreserveAspectRatio.Default;

        public List<SvgElement> Children { get; init; } = [];
    }
}
