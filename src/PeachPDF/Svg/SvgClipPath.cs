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
    /// A resolved <c>&lt;clipPath&gt;</c> definition - the shapes (or resolved <c>&lt;use&gt;</c>
    /// targets) that define the clip region. Lives only in <see cref="SvgDocument"/>'s clip-path
    /// registry, referenced by a <see cref="SvgElement.ClipPathRef"/>.
    /// </summary>
    internal sealed class SvgClipPath
    {
        public string? Id { get; init; }
        public List<SvgElement> Shapes { get; init; } = [];
    }
}
