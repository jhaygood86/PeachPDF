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
    }
}
