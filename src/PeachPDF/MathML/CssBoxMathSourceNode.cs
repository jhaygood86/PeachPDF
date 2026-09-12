using System.Collections.Generic;
using System.Linq;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Adapters;
using PeachPDF.Html.Adapters.Entities;

namespace PeachPDF.MathML
{
    /// <summary>
    /// Wraps a live <see cref="CssBox"/> (built for free by <see cref="Html.Core.Parse.HtmlParser"/> out
    /// of an inline <c>&lt;math&gt;</c>'s markup) as an <see cref="IMathSourceNode"/>. The wrapped boxes
    /// are read purely as a data source here - they are never laid out or painted through the generic
    /// box pipeline (see <c>CssBoxMath</c>).
    /// </summary>
    internal sealed class CssBoxMathSourceNode : IMathSourceNode
    {
        private readonly CssBox _box;

        public CssBoxMathSourceNode(CssBox box)
        {
            _box = box;
        }

        public string Name => _box.HtmlTag?.Name ?? "";

        public string? GetAttribute(string name) => _box.GetAttribute(name, null);

        public IEnumerable<KeyValuePair<string, string>> Attributes =>
            _box.HtmlTag?.Attributes ?? Enumerable.Empty<KeyValuePair<string, string>>();

        public IEnumerable<IMathSourceNode> Children =>
            _box.Boxes.Where(b => b.HtmlTag is not null).Select(b => (IMathSourceNode)new CssBoxMathSourceNode(b));

        public string GetTextContent() => string.Concat(_box.Boxes.Select(b => b.Text ?? string.Empty));

        public RColor Color => _box.ActualColor;

        public double FontSizePt => _box.ActualFont.Size;

        public RFont GetFontAtSize(double sizePt) => _box.GetActualFontAtSize(sizePt);
    }
}
