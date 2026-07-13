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
    }
}
