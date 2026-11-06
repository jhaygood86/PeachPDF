using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using PeachBrackets.Html.Dom;

namespace PeachBrackets.Svg.Smil
{
    /// <summary>
    /// An element of an SVG as the SMIL reader sees it: what animation elements are made of, whether the SVG is an inline one (boxes of the document) or a standalone
    /// one (an XML tree). Two nodes for the same element have the same <see cref="Key"/>.
    /// </summary>
    internal interface ISmilNode
    {
        string Name { get; }

        string? GetAttribute(string name);

        IEnumerable<ISmilNode> Children { get; }

        ISmilNode? Parent { get; }

        /// <summary>What identifies the element: the box, or the XML element.</summary>
        object Key { get; }

        /// <summary>The box of an inline SVG's element (what a colour keyword resolves against), null for a standalone one.</summary>
        CssBox? Box { get; }
    }

    internal sealed class CssBoxSmilNode(CssBox box) : ISmilNode
    {
        public string Name => box.HtmlTag?.Name ?? string.Empty;

        public string? GetAttribute(string name) => box.GetAttribute(name, null) ?? (name == "xlink:href" ? box.GetAttribute("href", null) : null);

        public IEnumerable<ISmilNode> Children => box.Boxes.Where(child => child.HtmlTag is not null).Select(child => new CssBoxSmilNode(child));

        public ISmilNode? Parent => box.ParentBox is { HtmlTag: not null } parent ? new CssBoxSmilNode(parent) : null;

        public object Key => box;

        public CssBox? Box => box;
    }

    internal sealed class XElementSmilNode(XElement element) : ISmilNode
    {
        private static readonly XNamespace Xlink = "http://www.w3.org/1999/xlink";

        public string Name => element.Name.LocalName;

        public string? GetAttribute(string name) =>
            name == "xlink:href" ? element.Attribute(Xlink + "href")?.Value ?? element.Attribute("href")?.Value : element.Attribute(name)?.Value;

        public IEnumerable<ISmilNode> Children => element.Elements().Select(child => new XElementSmilNode(child));

        public ISmilNode? Parent => element.Parent is { } parent ? new XElementSmilNode(parent) : null;

        public object Key => element;

        public CssBox? Box => null;
    }
}
