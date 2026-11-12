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
using System;
using System.Collections.Generic;

namespace PeachBrackets.Svg
{
    /// <summary>
    /// The parsed scene graph of an SVG document (or an inline <c>&lt;svg&gt;</c> subtree), built once
    /// by <see cref="SvgTreeBuilder"/> and reused across paints.
    /// </summary>
    internal sealed class SvgDocument
    {
        public Rect? ViewBox { get; set; }

        /// <summary>The root <c>&lt;svg&gt;</c> element's own <c>width</c>/<c>height</c>, if present (see <see cref="SvgValueParsers.ParseLength"/>).</summary>
        public double? Width { get; set; }
        public double? Height { get; set; }

        public SvgPreserveAspectRatio PreserveAspectRatio { get; set; } = SvgPreserveAspectRatio.Default;

        /// <summary>The root <c>&lt;svg&gt;</c>'s own <c>pointer-events</c>, or null when it declares none and takes its host box's.</summary>
        public PeachBrackets.Css.PointerEvents? PointerEvents { get; set; }

        public List<SvgElement> Children { get; init; } = [];

        /// <summary>
        /// A group standing for the root <c>&lt;svg&gt;</c> when it has a <c>perspective</c> or a <c>transform-style: preserve-3d</c> (the only things it carries that the page's
        /// own box for it does not draw): it holds <see cref="Children"/>, and is rendered in their place. Null otherwise.
        /// </summary>
        public SvgGroupElement? RootProjection { get; set; }

        /// <summary>Gradient definitions keyed by <c>id</c> (ids are case-sensitive per XML).</summary>
        public Dictionary<string, SvgGradient> Gradients { get; init; } = [with(StringComparer.Ordinal)];

        /// <summary>ClipPath definitions keyed by <c>id</c> (ids are case-sensitive per XML).</summary>
        public Dictionary<string, SvgClipPath> ClipPaths { get; init; } = [with(StringComparer.Ordinal)];

        /// <summary>Marker definitions keyed by <c>id</c> (ids are case-sensitive per XML).</summary>
        public Dictionary<string, SvgMarkerElement> Markers { get; init; } = [with(StringComparer.Ordinal)];

        /// <summary>Pattern definitions keyed by <c>id</c> (ids are case-sensitive per XML).</summary>
        public Dictionary<string, SvgPattern> Patterns { get; init; } = [with(StringComparer.Ordinal)];

        /// <summary>Mask definitions keyed by <c>id</c> (ids are case-sensitive per XML).</summary>
        public Dictionary<string, SvgMask> Masks { get; init; } = [with(StringComparer.Ordinal)];

        /// <summary>
        /// Filter definitions keyed by <c>id</c> (ids are case-sensitive per XML). Only ever contains a
        /// <c>&lt;filter&gt;</c> whose whole primitive graph is natively representable - see
        /// <see cref="SvgFilter"/>'s remarks.
        /// </summary>
        public Dictionary<string, SvgFilter> Filters { get; init; } = [with(StringComparer.Ordinal)];

        /// <summary>
        /// True when a filter reads <c>BackgroundImage</c>/<c>BackgroundAlpha</c>. What such a filter shows depends on what was painted
        /// behind the element, so the document's artwork cannot be painted once and shared as a cached form.
        /// </summary>
        public bool ReadsBackdrop { get; set; }

        /// <summary>
        /// True for a document built to be one moment of an animation (SMIL, or CSS animations of its elements, played by a clock): the next frame builds another, so
        /// painting it as a reusable form would only fill the cache with artwork nobody paints again.
        /// </summary>
        public bool IsTimeVarying { get; set; }

        /// <summary>
        /// The player that builds this standalone SVG again for each moment of its animations, when a clock plays the document that shows it; null for every other document.
        /// A box that shows the image paints <see cref="Current"/>.
        /// </summary>
        internal StandaloneSvgPlayer? Player { get; set; }

        /// <summary>The scene graph to paint now: the player's latest one for an animated image, this document otherwise.</summary>
        internal SvgDocument Current => Player?.Current ?? this;
    }
}
