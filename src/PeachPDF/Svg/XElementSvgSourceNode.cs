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
using PeachPDF.Html.Core.Parse;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace PeachPDF.Svg
{
    /// <summary>
    /// Wraps a <see cref="XElement"/> from a standalone SVG document (fetched for
    /// <c>&lt;img src="x.svg"&gt;</c>, parsed via <see cref="System.Xml.Linq"/> rather than the HTML
    /// tokenizer, since it's expected to be well-formed XML) as an <see cref="ISvgSourceNode"/>. Styling
    /// is matched through the full CSS engine against the SVG's own <see cref="CssData"/> (built from its
    /// nested <c>&lt;style&gt;</c> elements - host-document CSS does not apply to a standalone SVG) via a
    /// case-sensitive SVG-flavored <see cref="ICssDomNode"/> (<see cref="SvgXmlDomNode"/>).
    /// </summary>
    internal sealed class XElementSvgSourceNode : ISvgSourceNode
    {
        private static readonly XNamespace XlinkNamespace = "http://www.w3.org/1999/xlink";

        private readonly XElement _element;
        private readonly XElement _svgRoot;
        private readonly CssData? _cssData;
        private readonly string _media;

        public XElementSvgSourceNode(XElement element)
            : this(element, element, null, "print")
        {
        }

        internal XElementSvgSourceNode(XElement element, XElement svgRoot, CssData? cssData, string media)
        {
            _element = element;
            _svgRoot = svgRoot;
            _cssData = cssData;
            _media = media;
        }

        public string Name => _element.Name.LocalName;

        public string? GetAttribute(string name)
        {
            if (name == "xlink:href")
                return _element.Attribute(XlinkNamespace + "href")?.Value ?? _element.Attribute("href")?.Value;

            return _element.Attribute(name)?.Value;
        }

        public IEnumerable<ISvgSourceNode> Children =>
            _element.Elements().Select(e => (ISvgSourceNode)new XElementSvgSourceNode(e, _svgRoot, _cssData, _media));

        /// <summary>
        /// Only this element's own direct text-node children - deliberately NOT <see cref="XElement.Value"/>,
        /// which recurses into descendant elements' text too. Matches <see cref="CssBoxSvgSourceNode"/>'s
        /// (accidental but relied-upon) behavior: its own <c>GetTextContent</c> only sees direct child
        /// boxes, so a nested element's text never bleeds into its parent's. This matters once a node
        /// can have both loose text and element children (e.g. <c>&lt;text&gt;Hello &lt;tspan&gt;World&lt;/tspan&gt;&lt;/text&gt;</c>) -
        /// callers that need only the element's own run rely on that distinction (<see cref="SvgTreeBuilder"/>'s
        /// text handling).
        /// </summary>
        public string GetTextContent() => string.Concat(_element.Nodes().OfType<XText>().Select(t => t.Value));

        public IReadOnlyDictionary<string, string>? GetMatchedCssDeclarations() =>
            SvgCssStyling.GetMatchedDeclarations(new SvgXmlDomNode(_element, _svgRoot), _cssData, _media);

        public string? ResolveVar(string value) =>
            _cssData is null ? value : CssVarResolver.Resolve(new SvgXmlDomNode(_element, _svgRoot), value);
    }
}
