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

using System.Collections.Generic;

namespace PeachPDF.Svg
{
    /// <summary>
    /// A <c>&lt;mask&gt;</c> definition - never painted directly, only referenced by an element's
    /// <c>mask</c> attribute (see <see cref="SvgElement.MaskRef"/>). Unlike <see cref="SvgClipPath"/>
    /// (geometry-only), a mask's content is painted for real (fill/stroke/gradients and all), then its
    /// rendered luminosity (brightness) becomes the referencing element's opacity - white fully
    /// visible, black fully transparent - so <see cref="Children"/> is a full paintable scene graph,
    /// not just geometry.
    /// </summary>
    internal sealed class SvgMask
    {
        public string? Id { get; init; }

        /// <summary>Mask region. Interpreted per <see cref="MaskUnitsUserSpaceOnUse"/> - defaults (per spec) to -10%/-10%/120%/120% of the referencing element's bounding box.</summary>
        public double X { get; init; } = -0.1;
        public double Y { get; init; } = -0.1;
        public double Width { get; init; } = 1.2;
        public double Height { get; init; } = 1.2;

        /// <summary>False (the spec default) means the region above is <c>objectBoundingBox</c>-relative - fractions of the referencing element's bounding box.</summary>
        public bool MaskUnitsUserSpaceOnUse { get; init; }

        /// <summary>
        /// True (the spec default) means the mask's own content is painted in ordinary user-space
        /// units; <c>maskContentUnits="objectBoundingBox"</c> (false) would scale the mask's content
        /// coordinate system to the referencing element's bounding box - a rare combination, not
        /// resolved by this v1 implementation (content is always drawn as literal user-space units,
        /// same as if this were true) - a documented simplification, matching <see cref="SvgPattern.PatternContentUnitsUserSpaceOnUse"/>'s.
        /// </summary>
        public bool MaskContentUnitsUserSpaceOnUse { get; init; } = true;

        public List<SvgElement> Children { get; init; } = [];
    }
}
