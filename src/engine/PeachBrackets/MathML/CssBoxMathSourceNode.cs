using System;
using System.Collections.Generic;
using System.Linq;
using PeachBrackets.Css;
using PeachBrackets.Html.Dom;
using PeachDrawing.Core;

namespace PeachBrackets.MathML
{
    /// <summary>
    /// Wraps a live <see cref="CssBox"/> (built for free by <see cref="Html.Parse.HtmlParser"/> out
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

        public string GetTextContent() => _box.ForeignText ?? string.Concat(_box.Boxes.Select(b => b.Text ?? string.Empty));

        public PaintColor PaintColor => _box.ActualColor;

        public double FontSizePt => _box.ActualFont.Size;

        public bool? CssDisplayStyle
        {
            get
            {
                var style = _box.MathStyle;
                if (style.IsGlobalValue || style.IsUnresolved) return null;

                // The root is compared with what the UA sheet gives it (block normal, inline compact); anything else with its parent's value.
                var baseline = Name == "math"
                    ? (_box.GetAttribute("display", null) == "block" ? MathStyleMode.Normal : MathStyleMode.Compact)
                    : _box.ParentBox?.MathStyle is { IsGlobalValue: false, IsUnresolved: false } parentStyle ? parentStyle.Value : MathStyleMode.Compact;
                return style.Value == baseline ? null : style.Value == MathStyleMode.Normal;
            }
        }

        public string? CssMathDepth
        {
            get
            {
                var depth = _box.MathDepth;
                if (string.IsNullOrWhiteSpace(depth)) return null;
                var parent = Name == "math" ? "0" : _box.ParentBox?.MathDepth ?? "0";
                return string.Equals(depth.Trim(), parent.Trim(), StringComparison.OrdinalIgnoreCase) ? null : depth;
            }
        }

        public Font GetFontAtSize(double sizePt) => _box.GetActualFontAtSize(sizePt);
    }
}
