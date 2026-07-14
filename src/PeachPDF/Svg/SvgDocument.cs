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
using System;
using System.Collections.Generic;

namespace PeachPDF.Svg
{
    /// <summary>
    /// The parsed scene graph of an SVG document (or an inline <c>&lt;svg&gt;</c> subtree), built once
    /// by <see cref="SvgTreeBuilder"/> and reused across paints.
    /// </summary>
    internal sealed class SvgDocument
    {
        public RRect? ViewBox { get; set; }

        /// <summary>The root <c>&lt;svg&gt;</c> element's own <c>width</c>/<c>height</c>, if present (see <see cref="SvgValueParsers.ParseLength"/>).</summary>
        public double? Width { get; set; }
        public double? Height { get; set; }

        public SvgPreserveAspectRatio PreserveAspectRatio { get; set; } = SvgPreserveAspectRatio.Default;

        public List<SvgElement> Children { get; init; } = [];

        /// <summary>Gradient definitions keyed by <c>id</c> (ids are case-sensitive per XML).</summary>
        public Dictionary<string, SvgGradient> Gradients { get; init; } = new(StringComparer.Ordinal);

        /// <summary>ClipPath definitions keyed by <c>id</c> (ids are case-sensitive per XML).</summary>
        public Dictionary<string, SvgClipPath> ClipPaths { get; init; } = new(StringComparer.Ordinal);

        /// <summary>Marker definitions keyed by <c>id</c> (ids are case-sensitive per XML).</summary>
        public Dictionary<string, SvgMarkerElement> Markers { get; init; } = new(StringComparer.Ordinal);

        /// <summary>Pattern definitions keyed by <c>id</c> (ids are case-sensitive per XML).</summary>
        public Dictionary<string, SvgPattern> Patterns { get; init; } = new(StringComparer.Ordinal);

        /// <summary>Mask definitions keyed by <c>id</c> (ids are case-sensitive per XML).</summary>
        public Dictionary<string, SvgMask> Masks { get; init; } = new(StringComparer.Ordinal);
    }
}
