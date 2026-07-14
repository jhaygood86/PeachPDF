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
using System.Linq;
using System.Xml.Linq;

namespace PeachPDF.Svg
{
    /// <summary>
    /// Wraps a <see cref="XElement"/> from a standalone SVG document (fetched for
    /// <c>&lt;img src="x.svg"&gt;</c>, parsed via <see cref="System.Xml.Linq"/> rather than the HTML
    /// tokenizer, since it's expected to be well-formed XML) as an <see cref="ISvgSourceNode"/>.
    /// </summary>
    internal sealed class XElementSvgSourceNode(XElement element) : ISvgSourceNode
    {
        private static readonly XNamespace XlinkNamespace = "http://www.w3.org/1999/xlink";

        public string Name => element.Name.LocalName;

        public string? GetAttribute(string name)
        {
            if (name == "xlink:href")
                return element.Attribute(XlinkNamespace + "href")?.Value ?? element.Attribute("href")?.Value;

            return element.Attribute(name)?.Value;
        }

        public IEnumerable<ISvgSourceNode> Children => element.Elements().Select(static e => (ISvgSourceNode)new XElementSvgSourceNode(e));

        /// <summary>
        /// Only this element's own direct text-node children - deliberately NOT <see cref="XElement.Value"/>,
        /// which recurses into descendant elements' text too. Matches <see cref="CssBoxSvgSourceNode"/>'s
        /// (accidental but relied-upon) behavior: its own <c>GetTextContent</c> only sees direct child
        /// boxes, so a nested element's text never bleeds into its parent's. This matters once a node
        /// can have both loose text and element children (e.g. <c>&lt;text&gt;Hello &lt;tspan&gt;World&lt;/tspan&gt;&lt;/text&gt;</c>) -
        /// callers that need only the element's own run rely on that distinction (<see cref="SvgTreeBuilder"/>'s
        /// text handling).
        /// </summary>
        public string GetTextContent() => string.Concat(element.Nodes().OfType<XText>().Select(t => t.Value));
    }
}
