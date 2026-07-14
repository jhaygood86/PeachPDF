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

using PeachPDF.Html.Core.Dom;
using System.Collections.Generic;
using System.Linq;

namespace PeachPDF.Svg
{
    /// <summary>
    /// Wraps a live <see cref="CssBox"/> (built for free by <see cref="Html.Core.Parse.HtmlParser"/>
    /// out of an inline <c>&lt;svg&gt;</c>'s markup) as an <see cref="ISvgSourceNode"/>. The wrapped
    /// boxes are read purely as a data source here - they are never laid out or painted through the
    /// generic box pipeline.
    /// </summary>
    internal sealed class CssBoxSvgSourceNode(CssBox box) : ISvgSourceNode
    {
        public string Name => box.HtmlTag?.Name ?? "";

        public string? GetAttribute(string name) => box.GetAttribute(name, null);

        public IEnumerable<ISvgSourceNode> Children => box.Boxes.Select(static b => (ISvgSourceNode)new CssBoxSvgSourceNode(b));

        /// <summary>
        /// Mirrors <c>DomParser.CascadeParseStyles</c>'s own precedent for reading a <c>&lt;style&gt;</c>
        /// box's CSS text (direct child boxes are plain text nodes, not further nested elements - the
        /// HTML tokenizer doesn't give <c>&lt;style&gt;</c> any special raw-text handling, so its content
        /// arrives as ordinary child text boxes, same as any other element's text).
        /// </summary>
        public string GetTextContent() => string.Concat(box.Boxes.Select(b => b.Text ?? string.Empty));
    }
}
