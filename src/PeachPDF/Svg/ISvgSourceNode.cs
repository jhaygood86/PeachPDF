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
    /// A minimal, source-agnostic view of one node of an SVG element tree, so
    /// <see cref="SvgTreeBuilder"/> can build the same <see cref="SvgDocument"/> scene graph whether
    /// the underlying tree came from an inline <c>&lt;svg&gt;</c> already parsed into a
    /// <see cref="Html.Core.Dom.CssBox"/> tree, or from a standalone XML document fetched for an
    /// <c>&lt;img src="x.svg"&gt;</c>.
    /// </summary>
    internal interface ISvgSourceNode
    {
        /// <summary>The element's tag name, in its original document case (e.g. "linearGradient").</summary>
        string Name { get; }

        /// <summary>
        /// Gets the value of an attribute by its original-case name, or null if not present. For
        /// <c>xlink:href</c> specifically, pass the literal name <c>"xlink:href"</c> - implementations
        /// resolve it against their own source's namespace handling.
        /// </summary>
        string? GetAttribute(string name);

        IEnumerable<ISvgSourceNode> Children { get; }

        /// <summary>
        /// The concatenated text content of this node's children - only meaningful for a
        /// <c>&lt;style&gt;</c> element's CSS text today; empty for a node whose children are all
        /// elements rather than text.
        /// </summary>
        string GetTextContent();
    }
}
