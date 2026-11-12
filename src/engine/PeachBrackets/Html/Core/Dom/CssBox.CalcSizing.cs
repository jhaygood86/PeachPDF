using System;
using System.Globalization;
using System.Threading.Tasks;
using PeachBrackets.Css;
using PeachBrackets.Html.Parse;
using PeachDrawing.Core;

namespace PeachBrackets.Html.Dom
{
    /// <summary>
    /// <c>calc-size(&lt;basis&gt;, &lt;calc-sum&gt;)</c> on <c>width</c>/<c>height</c> and their min/max (CSS Values 5 §10.8.1).
    /// </summary>
    /// <remarks>
    /// Like <c>stretch</c> (<see cref="ResolveStretchSizes"/>), it is rewritten in place to a plain <c>calc()</c> the first time
    /// layout needs it, so no other code learns about it. A length or percentage basis rewrites at once; an intrinsic keyword
    /// (<c>auto</c>, <c>min-content</c>, <c>max-content</c>, <c>fit-content</c>) has to be measured, which needs the canvas, so the
    /// inline-axis ones are parked (the property reads its neutral value meanwhile) and resolved by <see cref="ResolveCalcSizeWidthsAsync"/>
    /// from <c>CssLayoutEngine.GetBoxWidth</c>. A block-axis keyword basis is the content height, so it is parked too and
    /// resolved by <see cref="ResolveCalcSizeHeights"/> from <c>CssLayoutEngine.ApplyHeight</c> once the content is laid out.
    /// </remarks>
    internal partial class CssBox
    {
        private string? _calcSizeWidth;
        private string? _calcSizeMinWidth;
        private string? _calcSizeMaxWidth;
        private string? _calcSizeHeight;
        private string? _calcSizeMinHeight;
        private string? _calcSizeMaxHeight;

        private static string RewriteCalcSize(string value, string neutral, bool measuredInline, out string? parked)
        {
            parked = null;
            if (!CalcSizeValue.TryParse(value, out var basis, out var expression)) return value;

            if (!CalcSizeValue.IsKeywordBasis(basis)) return CalcSizeValue.Substitute(expression, basis);

            if (measuredInline) parked = value;
            return neutral;
        }

        /// <summary>Rewrites every <c>calc-size()</c> size of this box that needs no measuring; parks the inline-axis keyword ones.</summary>
        internal void ResolveCalcSizes()
        {
            if (!CalcSizeValue.IsCalcSize(Width) && !CalcSizeValue.IsCalcSize(MinWidth) && !CalcSizeValue.IsCalcSize(MaxWidth)
                && !CalcSizeValue.IsCalcSize(Height) && !CalcSizeValue.IsCalcSize(MinHeight) && !CalcSizeValue.IsCalcSize(MaxHeight))
            {
                return;
            }

            if (CalcSizeValue.IsCalcSize(Width))
            {
                Width = RewriteCalcSize(Width, Keywords.Auto, true, out var parked);
                _calcSizeWidth = parked ?? _calcSizeWidth;
            }

            if (CalcSizeValue.IsCalcSize(MinWidth))
            {
                MinWidth = RewriteCalcSize(MinWidth, "0", true, out var parked);
                _calcSizeMinWidth = parked ?? _calcSizeMinWidth;
            }

            if (CalcSizeValue.IsCalcSize(MaxWidth))
            {
                MaxWidth = RewriteCalcSize(MaxWidth, Keywords.None, true, out var parked);
                _calcSizeMaxWidth = parked ?? _calcSizeMaxWidth;
            }

            // A block-axis keyword basis is the box's content height, which only exists once its content is laid out:
            // parked here, resolved by ResolveCalcSizeHeights from ApplyHeight in the epilogue.
            if (CalcSizeValue.IsCalcSize(Height))
            {
                Height = RewriteCalcSize(Height, Keywords.Auto, true, out var parked);
                _calcSizeHeight = parked ?? _calcSizeHeight;
            }

            if (CalcSizeValue.IsCalcSize(MinHeight))
            {
                MinHeight = RewriteCalcSize(MinHeight, "0", true, out var parked);
                _calcSizeMinHeight = parked ?? _calcSizeMinHeight;
            }

            if (CalcSizeValue.IsCalcSize(MaxHeight))
            {
                MaxHeight = RewriteCalcSize(MaxHeight, Keywords.None, true, out var parked);
                _calcSizeMaxHeight = parked ?? _calcSizeMaxHeight;
            }
        }

        /// <summary>
        /// Resolves the parked block-axis <c>calc-size()</c> sizes against the content height. Runs from <c>CssLayoutEngine.ApplyHeight</c>,
        /// where <c>ActualBottom</c> is still the content-driven bottom because the declared heights were neutral during layout.
        /// All four keywords give the same block size: the box's auto height.
        /// </summary>
        internal void ResolveCalcSizeHeights()
        {
            if (_calcSizeHeight is null && _calcSizeMinHeight is null && _calcSizeMaxHeight is null) return;

            var (height, minHeight, maxHeight) = (_calcSizeHeight, _calcSizeMinHeight, _calcSizeMaxHeight);
            _calcSizeHeight = _calcSizeMinHeight = _calcSizeMaxHeight = null;

            var unitsPerPoint = HtmlContainer?.RenderContext?.LayoutUnitsPerPoint ?? 1.0;
            var content = Math.Max(0, ActualBottom - Location.Y - ActualBoxSizeIncludedHeight) / unitsPerPoint;
            var sizeText = content.ToString("F4", CultureInfo.InvariantCulture) + "pt";

            string Resolve(string calcSize)
            {
                CalcSizeValue.TryParse(calcSize, out _, out var expression);
                return CalcSizeValue.Substitute(expression, sizeText);
            }

            if (height is not null) Height = Resolve(height);
            if (minHeight is not null) MinHeight = Resolve(minHeight);
            if (maxHeight is not null) MaxHeight = Resolve(maxHeight);
        }

        /// <summary>Resolves the parked inline-axis <c>calc-size()</c> sizes by measuring their keyword basis.</summary>
        internal async ValueTask ResolveCalcSizeWidthsAsync(Canvas g, double? blockTop)
        {
            if (_calcSizeWidth is null && _calcSizeMinWidth is null && _calcSizeMaxWidth is null) return;

            var (width, minWidth, maxWidth) = (_calcSizeWidth, _calcSizeMinWidth, _calcSizeMaxWidth);
            _calcSizeWidth = _calcSizeMinWidth = _calcSizeMaxWidth = null;

            // What `auto` gives this box: its width with none of the declared sizes in play.
            var declared = (Width, MinWidth, MaxWidth);
            Width = Keywords.Auto;
            MinWidth = "0";
            MaxWidth = Keywords.None;
            double autoWidth;
            try
            {
                autoWidth = await CssLayoutEngine.GetBoxWidth(g, this, blockTop);
            }
            finally
            {
                (Width, MinWidth, MaxWidth) = declared;
            }

            var included = ActualBoxSizeIncludedWidth;
            var unitsPerPoint = HtmlContainer?.RenderContext?.LayoutUnitsPerPoint ?? 1.0;

            async ValueTask<string> Resolve(string calcSize)
            {
                CalcSizeValue.TryParse(calcSize, out var basis, out var expression);

                double size;
                if (basis.Equals("min-content", StringComparison.OrdinalIgnoreCase))
                {
                    size = await CssLayoutEngine.GetMinContentWidth(g, this) - included;
                }
                else if (basis.Equals("max-content", StringComparison.OrdinalIgnoreCase))
                {
                    size = await CssLayoutEngine.GetMaxContentWidth(g, this) - included;
                }
                else if (basis.Equals("fit-content", StringComparison.OrdinalIgnoreCase))
                {
                    var min = await CssLayoutEngine.GetMinContentWidth(g, this);
                    var max = await CssLayoutEngine.GetMaxContentWidth(g, this);
                    size = Math.Min(max, Math.Max(min, autoWidth + included)) - included;
                }
                else
                {
                    size = autoWidth;
                }

                var points = Math.Max(0, size) / unitsPerPoint;
                return CalcSizeValue.Substitute(expression, points.ToString("F4", CultureInfo.InvariantCulture) + "pt");
            }

            if (width is not null) Width = await Resolve(width);
            if (minWidth is not null) MinWidth = await Resolve(minWidth);
            if (maxWidth is not null) MaxWidth = await Resolve(maxWidth);
        }
    }
}
