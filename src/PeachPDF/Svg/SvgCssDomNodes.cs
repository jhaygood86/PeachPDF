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

using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Parse;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Xml.Linq;

namespace PeachPDF.Svg
{
    /// <summary>
    /// An <see cref="ICssDomNode"/> over one element of an inline <c>&lt;svg&gt;</c>'s live
    /// <see cref="CssBox"/> subtree, so the HTML selector engine can match SVG shapes. Unlike
    /// <see cref="CssBox"/>'s own <see cref="ICssDomNode"/> view (which reports ASCII-case-insensitive
    /// HTML matching), this reports <see cref="StringComparison.Ordinal"/> case-sensitive matching, per
    /// SVG's XML rules. Navigation stays SVG-flavored within the <c>&lt;svg&gt;</c> subtree; at the
    /// <c>&lt;svg&gt;</c> root it crosses into the surrounding HTML by returning the raw
    /// <see cref="CssBox"/> parent (so an ancestor selector like <c>.wrap svg rect</c> still matches, each
    /// segment with its own language's case-sensitivity). Created per-navigation, so equality/hashing key
    /// off the wrapped box identity (the matcher relies on sibling <c>IndexOf</c>).
    /// </summary>
    internal sealed class SvgCssBoxDomNode(CssBox box, CssBox svgRoot) : ICssDomNode
    {
        public string? TagName => box.HtmlTag?.Name;

        public StringComparison NameComparison => StringComparison.Ordinal;

        public string? GetAttribute(string name)
        {
            // Case-sensitive lookup (SVG/XML), over HtmlTag.Attributes' case-preserving (but
            // OrdinalIgnoreCase-keyed) storage - so the exact-case key must be found explicitly.
            var attributes = box.HtmlTag?.Attributes;
            if (attributes is null) return null;
            foreach (var attribute in attributes)
                if (string.Equals(attribute.Key, name, StringComparison.Ordinal))
                    return attribute.Value;
            return null;
        }

        public ICssDomNode? Parent =>
            ReferenceEquals(box, svgRoot)
                ? box.ParentBox // crossing out of the SVG fragment into HTML: raw CssBox (case-insensitive)
                : box.ParentBox is null ? null : new SvgCssBoxDomNode(box.ParentBox, svgRoot);

        public IReadOnlyList<ICssDomNode> Children =>
            box.Boxes.Select(ICssDomNode (b) => new SvgCssBoxDomNode(b, svgRoot)).ToList();

        public bool IsRoot => false;

        public Dictionary<string, string>? CustomProperties
        {
            get => box.CustomProperties;
            set => box.CustomProperties = value;
        }

        public override bool Equals(object? obj) => obj is SvgCssBoxDomNode other && ReferenceEquals(other.WrappedBox, box);
        public override int GetHashCode() => RuntimeHelpers.GetHashCode(box);
        private CssBox WrappedBox => box;
    }

    /// <summary>
    /// An <see cref="ICssDomNode"/> over one element of a standalone SVG's <see cref="XElement"/> tree.
    /// Case-sensitive (SVG/XML) matching; navigation is confined to the SVG document (the root's parent is
    /// null). Custom properties are stored per element via an <see cref="XElement"/> annotation (the
    /// wrappers are transient), populated by <c>SvgCssStyling.CascadeCustomProperties</c>.
    /// </summary>
    internal sealed class SvgXmlDomNode(XElement element, XElement svgRoot) : ICssDomNode
    {
        private static readonly XNamespace XlinkNamespace = "http://www.w3.org/1999/xlink";

        public string? TagName => element.Name.LocalName;

        public StringComparison NameComparison => StringComparison.Ordinal;

        public string? GetAttribute(string name)
        {
            if (name == "xlink:href")
                return element.Attribute(XlinkNamespace + "href")?.Value ?? element.Attribute("href")?.Value;
            return element.Attribute(name)?.Value; // XName lookup is case-sensitive
        }

        public ICssDomNode? Parent =>
            ReferenceEquals(element, svgRoot) || element.Parent is null
                ? null
                : new SvgXmlDomNode(element.Parent, svgRoot);

        public IReadOnlyList<ICssDomNode> Children =>
            element.Elements().Select(ICssDomNode (e) => new SvgXmlDomNode(e, svgRoot)).ToList();

        public bool IsRoot => false;

        public Dictionary<string, string>? CustomProperties
        {
            get => element.Annotation<CustomPropsHolder>()?.Value;
            set
            {
                element.RemoveAnnotations<CustomPropsHolder>();
                if (value is not null)
                    element.AddAnnotation(new CustomPropsHolder(value));
            }
        }

        public override bool Equals(object? obj) => obj is SvgXmlDomNode other && ReferenceEquals(other.WrappedElement, element);
        public override int GetHashCode() => RuntimeHelpers.GetHashCode(element);
        private XElement WrappedElement => element;

        private sealed class CustomPropsHolder(Dictionary<string, string> value)
        {
            public Dictionary<string, string> Value { get; } = value;
        }
    }

    /// <summary>
    /// Shared helpers that resolve SVG styling through the full HTML CSS engine (selector matching plus
    /// <c>var()</c>), rather than a parallel matcher.
    /// </summary>
    internal static class SvgCssStyling
    {
        /// <summary>
        /// The author-stylesheet declarations that apply to <paramref name="node"/>, merged winner-last
        /// (<see cref="CssData.GetAuthorStyleRules"/> returns them specificity- then source-order-sorted),
        /// with each value's <c>var()</c> references resolved against the node's custom properties. Custom
        /// property (<c>--*</c>) declarations are excluded (they participate via <c>var()</c> resolution,
        /// not as SVG paint properties). Null when there is no CSS context (e.g. a geometry-only source).
        /// </summary>
        public static IReadOnlyDictionary<string, string>? GetMatchedDeclarations(ICssDomNode node, CssData? cssData, string media)
        {
            if (cssData is null) return null;

            // CSS property names are case-insensitive (unlike SVG selectors), so key case-insensitively.
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var rule in cssData.GetAuthorStyleRules(media, node))
            {
                foreach (var property in rule.Style)
                {
                    if (property.Name.StartsWith("--", StringComparison.Ordinal)) continue;

                    var resolved = CssVarResolver.Resolve(node, property.Value);
                    if (resolved is not null)
                        result[property.Name] = resolved;
                    else
                        result.Remove(property.Name); // a guaranteed-invalid var() drops the declaration
                }
            }

            return result;
        }

        /// <summary>
        /// The concatenated CSS text of every <c>&lt;style&gt;</c> element anywhere in a standalone SVG's
        /// <see cref="XElement"/> tree, in document order (namespace-agnostic - matches on local name).
        /// </summary>
        public static string CollectStyleText(XElement root)
        {
            var builder = new StringBuilder();
            foreach (var style in root.DescendantsAndSelf().Where(e => e.Name.LocalName == "style"))
                builder.Append(style.Value).Append('\n');
            return builder.ToString();
        }

        /// <summary>
        /// Builds an SVG-local, author-origin <see cref="CssData"/> from a standalone SVG's own
        /// <c>&lt;style&gt;</c> text (no HTML UA sheet - a standalone SVG's styling is entirely its own,
        /// and skipping the UA sheet avoids HTML pseudo-element synthesis noise). Null when there is no
        /// <c>&lt;style&gt;</c> content, in which case only presentation/<c>style=""</c> attributes apply.
        /// </summary>
        public static CssData? BuildStyleData(string? styleText)
        {
            if (string.IsNullOrWhiteSpace(styleText)) return null;

            var cssData = new CssData();
            cssData.Stylesheets.Add(CssParser.ParseStyleSheet(styleText));
            return cssData;
        }

        /// <summary>
        /// Populates <see cref="ICssDomNode.CustomProperties"/> on every element of a standalone SVG's
        /// <see cref="XElement"/> tree so <c>var()</c> works there (an inline <c>&lt;svg&gt;</c>'s boxes
        /// already carry theirs from the HTML cascade). Walks top-down, inheriting the parent's dictionary
        /// and overlaying each element's own matched <c>--*</c> rules then its inline <c>style=""</c>
        /// custom properties. Values are stored raw (they may themselves contain <c>var()</c>), matching the
        /// HTML cascade.
        /// </summary>
        public static void CascadeCustomProperties(XElement root, CssData? cssData, string media)
        {
            CascadeCustomProperties(new SvgXmlDomNode(root, root), cssData, media, null);
        }

        private static void CascadeCustomProperties(SvgXmlDomNode node, CssData? cssData, string media, Dictionary<string, string>? inherited)
        {
            Dictionary<string, string>? own = inherited is null ? null : new Dictionary<string, string>(inherited);

            if (cssData is not null)
            {
                foreach (var rule in cssData.GetAuthorStyleRules(media, node))
                    foreach (var property in rule.Style)
                        if (property.Name.StartsWith("--", StringComparison.Ordinal))
                            (own ??= new Dictionary<string, string>())[property.Name] = property.Value;
            }

            foreach (var (name, value) in SvgValueParsers.ParseStyleDeclarations(node.GetAttribute("style")))
                if (name.StartsWith("--", StringComparison.Ordinal))
                    (own ??= new Dictionary<string, string>())[name] = value;

            node.CustomProperties = own;

            foreach (var child in node.Children)
                if (child is SvgXmlDomNode childNode)
                    CascadeCustomProperties(childNode, cssData, media, own);
        }
    }
}
