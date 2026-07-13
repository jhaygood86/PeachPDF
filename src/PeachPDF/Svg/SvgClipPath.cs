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
    /// A resolved <c>&lt;clipPath&gt;</c> definition - the shapes (or resolved <c>&lt;use&gt;</c>
    /// targets) that define the clip region. Lives only in <see cref="SvgDocument"/>'s clip-path
    /// registry, referenced by a <see cref="SvgElement.ClipPathRef"/>.
    /// </summary>
    internal sealed class SvgClipPath
    {
        public string? Id { get; init; }
        public List<SvgElement> Shapes { get; init; } = [];

        /// <summary>
        /// The <c>clip-rule</c> read from the <c>&lt;clipPath&gt;</c> element itself (defaulting to
        /// <see cref="RFillMode.Nonzero"/>) - applied to the whole combined clip region, since all
        /// of a clipPath's children are appended into a single <c>RGraphicsPath</c> rather than
        /// tracked individually. Per-child <c>clip-rule</c> overrides are not supported.
        /// </summary>
        public RFillMode ClipRule { get; init; } = RFillMode.Nonzero;
    }
}
