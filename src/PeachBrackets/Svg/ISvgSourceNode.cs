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
        /// This node's direct children - loose text nodes AND element nodes - in document order. Unlike
        /// <see cref="Children"/> (elements only) and <see cref="GetTextContent"/> (own text flattened,
        /// losing its position relative to child elements), this preserves the interleaving of text and
        /// elements, which SVG text layout needs (e.g. the space in <c>&lt;text&gt;a &lt;tspan&gt;b&lt;/tspan&gt; c&lt;/text&gt;</c>,
        /// and the ordering of text authored after a child element).
        /// </summary>
        IEnumerable<SvgContentNode> ContentNodes { get; }

        /// <summary>
        /// The concatenated text content of this node's children - only meaningful for a
        /// <c>&lt;style&gt;</c> element's CSS text today; empty for a node whose children are all
        /// elements rather than text.
        /// </summary>
        string GetTextContent();

        /// <summary>
        /// The author-stylesheet declarations (property → value, cascade-resolved winner-last, with
        /// <c>var()</c> already substituted) that apply to this node, matched through the full CSS engine
        /// against the relevant <c>CssData</c> (the host document's for inline <c>&lt;svg&gt;</c>, the
        /// SVG's own for standalone). Null when there is no CSS context (a geometry-only source). Consumed
        /// as the middle tier of <see cref="SvgTreeBuilder"/>'s <c>style=""</c> &gt; matched-rule &gt;
        /// presentation-attribute precedence. A property present with a <c>null</c> value is the winning
        /// declaration but invalid at computed-value time (guaranteed-invalid <c>var()</c>) — the consumer
        /// computes it to inherited/initial rather than consulting the presentation attribute.
        /// </summary>
        IReadOnlyDictionary<string, string?>? GetMatchedCssDeclarations() => null;

        /// <summary>
        /// Whether the winning matched author-stylesheet declaration of <paramref name="name"/> is <c>!important</c>, which makes it beat
        /// an inline <c>style=""</c> declaration that is not (CSS Cascade 4, section 6.1).
        /// </summary>
        bool IsMatchedCssDeclarationImportant(string name) => false;

        /// <summary>
        /// Resolves any <c>var()</c> references in a raw value (e.g. from a <c>style=""</c> attribute)
        /// against this node's cascaded custom properties; returns the value unchanged when there is no
        /// CSS context, or null if the value is guaranteed-invalid.
        /// </summary>
        string? ResolveVar(string value) => value;

        /// <summary>Whether a CSS animation is in play on this element at the sampled point, which settles at no cost the (common) case of none, before anything is allocated for the lookup.</summary>
        bool IsAnimated => false;

        /// <summary>The parent element within the same SVG tree, or null at the root (and for a source that does not track it).</summary>
        ISvgSourceNode? Parent => null;

        /// <summary>
        /// The value a CSS animation gives <paramref name="name"/> on this element at the sampled point, or null when no animation does (the
        /// default, and every element of a standalone SVG, which is not animated). The animation origin sits above normal declarations and below
        /// <c>!important</c> ones (CSS Cascade 4, section 6.1), which the caller settles.
        /// </summary>
        /// <param name="name">The property.</param>
        /// <param name="underlying">The value without the animation: what a keyframe that omits the property stands for. Called at most once.</param>
        /// <param name="parent">The parent element's value, which <c>inherit</c> in a keyframe takes.</param>
        /// <param name="initial">The initial value, which <c>initial</c> in a keyframe takes.</param>
        /// <param name="inherited">Whether the property is inherited.</param>
        string? GetAnimatedValue(string name, Func<string?> underlying, Func<string?> parent, Func<string?> initial, bool inherited) => null;

        /// <summary>
        /// The language this document/embedding context resolves to when nothing in the SVG's own tree
        /// sets <c>lang</c>/<c>xml:lang</c> - the root's seed value for <see cref="SvgTreeBuilder"/>'s own
        /// per-element language inheritance (mirrors <c>CssBox.Language</c>'s GSUB per-language feature
        /// selection). Null (the default) for a source with no such context - a standalone SVG document
        /// has nothing to fall back to beyond its own tree, so its text always resolves each script's
        /// default <c>LangSys</c> unless something in the SVG itself sets <c>lang</c>/<c>xml:lang</c>.
        /// </summary>
        string? DocumentLanguageFallback => null;
    }

    /// <summary>
    /// One direct child of an <see cref="ISvgSourceNode"/> as seen by <see cref="ISvgSourceNode.ContentNodes"/>:
    /// either a loose text node (<see cref="Text"/> set, <see cref="Element"/> null) or a child element
    /// (<see cref="Element"/> set, <see cref="Text"/> null).
    /// </summary>
    internal readonly struct SvgContentNode
    {
        private SvgContentNode(string? text, ISvgSourceNode? element)
        {
            Text = text;
            Element = element;
        }

        /// <summary>The raw (un-collapsed) text of a text node, or null when this is an element node.</summary>
        public string? Text { get; }

        /// <summary>The child element, or null when this is a text node.</summary>
        public ISvgSourceNode? Element { get; }

        /// <summary>True when this is a text node (<see cref="Text"/> is set).</summary>
        public bool IsText => Element is null;

        public static SvgContentNode OfText(string text) => new(text, null);
        public static SvgContentNode OfElement(ISvgSourceNode element) => new(null, element);
    }
}
