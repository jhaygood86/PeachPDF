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

using System;
using System.Collections.Generic;

namespace PeachPDF.Html.Core
{
    /// <summary>
    /// A source-agnostic view of one element node, sufficient for CSS selector matching
    /// (<see cref="CssData.GetAuthorStyleRules"/> / <c>DoesSelectorMatch</c>) and <c>var()</c>
    /// resolution (<c>CssVarResolver</c>). Implemented by <see cref="Dom.CssBox"/> (the HTML box tree)
    /// and by the SVG subsystem's own nodes (both an inline <see cref="Dom.CssBox"/>-backed adapter and
    /// a standalone <c>XElement</c>-backed adapter), so the one selector engine serves HTML and SVG
    /// alike instead of SVG duplicating a parallel matcher.
    /// </summary>
    /// <remarks>
    /// Case-sensitivity is per node via <see cref="NameComparison"/>: HTML (<see cref="Dom.CssBox"/>)
    /// matches ASCII case-insensitively, but SVG is XML and matches case-sensitively
    /// (Selectors 4 §6). SVG adapter nodes are created per-navigation, so implementations that wrap an
    /// underlying object MUST override <see cref="object.Equals(object)"/>/<see cref="object.GetHashCode"/>
    /// on that underlying identity - the matcher relies on sibling <c>IndexOf</c>/equality.
    /// </remarks>
    internal interface ICssDomNode
    {
        /// <summary>The element's tag name in its original document case, or null for a non-element (anonymous/text) node.</summary>
        string? TagName { get; }

        /// <summary>Gets an attribute value (or null if absent), compared using <see cref="NameComparison"/>.</summary>
        string? GetAttribute(string name);

        /// <summary>How element/attribute/class/id names and attribute values are compared for this node's language.</summary>
        StringComparison NameComparison { get; }

        /// <summary>The parent node, or null at the (matching-scope) root.</summary>
        ICssDomNode? Parent { get; }

        /// <summary>This node's child nodes in document order (may include non-element nodes; callers filter by <see cref="TagName"/>).</summary>
        IReadOnlyList<ICssDomNode> Children { get; }

        /// <summary>True for the synthetic document-root wrapper, which the universal selector must never match.</summary>
        bool IsRoot { get; }

        /// <summary>
        /// True when this element has no children other than white-space-only text nodes and comments -
        /// the <c>:empty</c> test (Selectors 4 §9.5).
        /// </summary>
        /// <remarks>
        /// Asked of the node rather than derived from <see cref="Children"/> because what counts as child
        /// content differs per tree. The HTML box tree keeps text in anonymous child boxes and may already
        /// have grown a generated <c>::before</c>/<c>::after</c>/<c>::marker</c> box that <c>:empty</c> must
        /// ignore (it is defined over the source tree), while a standalone SVG's <see cref="Children"/>
        /// deliberately exposes elements only, leaving its text nodes invisible to a generic walk.
        /// </remarks>
        bool IsEmpty { get; }

        /// <summary>The node's CSS custom properties (<c>--x</c>), inherited+overlaid by the cascade; read by <c>var()</c> resolution.</summary>
        Dictionary<string, string>? CustomProperties { get; set; }
    }
}
