using PeachPDF.Adapters;
using PeachPDF.CSS;
using PeachPDF.Html.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core.Parse;
using PeachPDF.Html.Core.Utils;
using PeachPDF.Text;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Globalization;
using System.Linq;
using System.Text;

namespace PeachPDF.Html.Core.Dom
{
    /// <summary>
    /// Values calculated from a <see cref="CssBox"/>'s <see cref="ComputedStyle"/> - the <c>Actual*</c>
    /// family (<see cref="ActualColor"/>, <see cref="ActualFont"/>, <see cref="ActualBorderTopWidth"/>,
    /// etc.), lazily computed and cached the first time they're read, invalidated by the owning
    /// <see cref="CssBox"/> when the cascaded property they derive from is rewritten. One instance per
    /// <see cref="CssBox"/>, created once in its constructor and never cloned or shared - unlike
    /// <see cref="ComputedStyle"/>, there is no "default" shared instance here, since these values are
    /// inherently specific to the box that owns them (they read <see cref="Owner"/>'s <see cref="CssBox.ParentBox"/>
    /// chain and <see cref="CssBox.HtmlContainer"/>, not just its own <see cref="ComputedStyle"/>).
    /// <para>
    /// Layout-engine output (<c>Location</c>, <c>Size</c>, <c>Bounds</c>, etc.) is NOT here - those are
    /// assigned directly by the layout algorithm rather than calculated from <see cref="ComputedStyle"/>,
    /// so they remain plain members on <see cref="CssBox"/>.
    /// </para>
    /// </summary>
    internal sealed record DerivedStyle(CssBox Owner) : IFontMetricSource
    {
        private ComputedStyle Style => Owner.ComputedStyle;

        #region Border widths

        private double _actualBorderTopWidth = double.NaN;
        private double _actualBorderRightWidth = double.NaN;
        private double _actualBorderBottomWidth = double.NaN;
        private double _actualBorderLeftWidth = double.NaN;
        private double _actualColumnRuleWidth = double.NaN;

        public double ActualBorderTopWidth
        {
            get
            {
                if (!double.IsNaN(_actualBorderTopWidth)) return _actualBorderTopWidth;

                _actualBorderTopWidth = CssValueParser.GetActualBorderWidth(Style.Border.BorderTopWidth, Owner);
                if (Style.Border.BorderTopStyle.Value is LineStyle.None or LineStyle.Hidden)
                    _actualBorderTopWidth = 0f;

                return _actualBorderTopWidth;
            }
        }

        public double ActualBorderRightWidth
        {
            get
            {
                if (!double.IsNaN(_actualBorderRightWidth)) return _actualBorderRightWidth;

                _actualBorderRightWidth = CssValueParser.GetActualBorderWidth(Style.Border.BorderRightWidth, Owner);
                if (Style.Border.BorderRightStyle.Value is LineStyle.None or LineStyle.Hidden)
                    _actualBorderRightWidth = 0f;

                return _actualBorderRightWidth;
            }
        }

        public double ActualBorderBottomWidth
        {
            get
            {
                if (!double.IsNaN(_actualBorderBottomWidth)) return _actualBorderBottomWidth;

                _actualBorderBottomWidth = CssValueParser.GetActualBorderWidth(Style.Border.BorderBottomWidth, Owner);
                if (Style.Border.BorderBottomStyle.Value is LineStyle.None or LineStyle.Hidden)
                    _actualBorderBottomWidth = 0f;

                return _actualBorderBottomWidth;
            }
        }

        public double ActualBorderLeftWidth
        {
            get
            {
                if (!double.IsNaN(_actualBorderLeftWidth)) return _actualBorderLeftWidth;

                _actualBorderLeftWidth = CssValueParser.GetActualBorderWidth(Style.Border.BorderLeftWidth, Owner);
                if (Style.Border.BorderLeftStyle.Value is LineStyle.None or LineStyle.Hidden)
                    _actualBorderLeftWidth = 0f;

                return _actualBorderLeftWidth;
            }
        }

        /// <summary>Actual column-rule width (the line drawn between columns in a multi-column container).</summary>
        public double ActualColumnRuleWidth
        {
            get
            {
                if (!double.IsNaN(_actualColumnRuleWidth)) return _actualColumnRuleWidth;

                _actualColumnRuleWidth = CssValueParser.GetActualBorderWidth(Style.MultiColumn.ColumnRuleWidth, Owner);
                if (Style.MultiColumn.ColumnRuleStyle.Value is LineStyle.None or LineStyle.Hidden)
                    _actualColumnRuleWidth = 0f;

                return _actualColumnRuleWidth;
            }
        }

        /// <summary>
        /// The declared border width CSS 2.1 §17.6.2 resolution itself needs as an input - deliberately
        /// bypassing <see cref="_actualBorderTopWidth"/>'s cache, which <see cref="SetCollapsedUsedBorderWidths"/>
        /// overwrites with the box-model *used* half-width and does not restore while the table stays
        /// collapsed. Every §17.6.2 candidate reads this instead, including
        /// <see cref="CollapsedBorderModel.Resolve"/>'s own: a resolution is that override's *input*, so
        /// reading the cache feeds the previous resolution's output back in - harmless on a table's
        /// first layout pass, and a silent halving on every pass after it (<c>ShrinkToFit</c>, a §4.3
        /// relocation and a per-page-width reflow all re-enter the engine over the same boxes).
        /// </summary>
        internal double NaturalBorderTopWidth =>
            Style.Border.BorderTopStyle.Value is LineStyle.None or LineStyle.Hidden
                ? 0d : CssValueParser.GetActualBorderWidth(Style.Border.BorderTopWidth, Owner);

        internal double NaturalBorderRightWidth =>
            Style.Border.BorderRightStyle.Value is LineStyle.None or LineStyle.Hidden
                ? 0d : CssValueParser.GetActualBorderWidth(Style.Border.BorderRightWidth, Owner);

        internal double NaturalBorderBottomWidth =>
            Style.Border.BorderBottomStyle.Value is LineStyle.None or LineStyle.Hidden
                ? 0d : CssValueParser.GetActualBorderWidth(Style.Border.BorderBottomWidth, Owner);

        internal double NaturalBorderLeftWidth =>
            Style.Border.BorderLeftStyle.Value is LineStyle.None or LineStyle.Hidden
                ? 0d : CssValueParser.GetActualBorderWidth(Style.Border.BorderLeftWidth, Owner);

        internal void InvalidateBorderTopWidth() { if (!_hasCollapsedUsedBorderWidths) _actualBorderTopWidth = double.NaN; }
        internal void InvalidateBorderRightWidth() { if (!_hasCollapsedUsedBorderWidths) _actualBorderRightWidth = double.NaN; }
        internal void InvalidateBorderBottomWidth() { if (!_hasCollapsedUsedBorderWidths) _actualBorderBottomWidth = double.NaN; }
        internal void InvalidateBorderLeftWidth() { if (!_hasCollapsedUsedBorderWidths) _actualBorderLeftWidth = double.NaN; }

        private bool _hasCollapsedUsedBorderWidths;

        /// <summary>
        /// States this box's <i>used</i> border widths under CSS 2.1 §17.6.2's collapsing model - half the
        /// resolved grid-line width on each edge, rather than the box's own computed <c>border-*-width</c>
        /// (which <see cref="CollapsedBorderModel"/> still needs to read as a resolver *input*, so this
        /// overrides the derived <c>Actual*Width</c> family only, never the declared style itself). Every
        /// existing reader of <c>ActualBorderTopWidth</c>/etc - <c>ClientLeft</c>/<c>ClientTop</c>, content
        /// insets, <c>GetAvailableCellWidth</c>, <c>GetWidthSum</c> - then sees the used value with no
        /// call-site change. Set once per table per layout pass by <c>CssLayoutEngineTable</c>, before any
        /// cell is measured or laid out; <see cref="InvalidateBorderTopWidth"/> and its three siblings
        /// become no-ops while this is active, so an incidental style re-application mid-pass cannot wipe
        /// a stated value back to "recompute from the declared border".
        /// </summary>
        internal void SetCollapsedUsedBorderWidths(double top, double right, double bottom, double left)
        {
            _actualBorderTopWidth = top;
            _actualBorderRightWidth = right;
            _actualBorderBottomWidth = bottom;
            _actualBorderLeftWidth = left;
            _hasCollapsedUsedBorderWidths = true;
        }

        /// <summary>Undoes <see cref="SetCollapsedUsedBorderWidths"/> - called on every box that no longer participates in a collapsed table (or never did), so its border widths resolve normally again.</summary>
        internal void ClearCollapsedUsedBorderWidths()
        {
            _hasCollapsedUsedBorderWidths = false;
            _actualBorderTopWidth = double.NaN;
            _actualBorderRightWidth = double.NaN;
            _actualBorderBottomWidth = double.NaN;
            _actualBorderLeftWidth = double.NaN;
        }

        #endregion

        #region Border colors

        private RColor _actualBorderTopColor = RColor.Empty;
        private RColor _actualBorderRightColor = RColor.Empty;
        private RColor _actualBorderBottomColor = RColor.Empty;
        private RColor _actualBorderLeftColor = RColor.Empty;
        private RColor _actualColumnRuleColor = RColor.Empty;

        public RColor ActualBorderTopColor
        {
            get
            {
                if (_actualBorderTopColor.IsEmpty)
                    _actualBorderTopColor = ResolveBorderSideColor(Style.Border.BorderTopColor, Style.Border.BorderTopStyle.Value);
                return _actualBorderTopColor;
            }
        }

        public RColor ActualBorderRightColor
        {
            get
            {
                if (_actualBorderRightColor.IsEmpty)
                    _actualBorderRightColor = ResolveBorderSideColor(Style.Border.BorderRightColor, Style.Border.BorderRightStyle.Value);
                return _actualBorderRightColor;
            }
        }

        public RColor ActualBorderBottomColor
        {
            get
            {
                if (_actualBorderBottomColor.IsEmpty)
                    _actualBorderBottomColor = ResolveBorderSideColor(Style.Border.BorderBottomColor, Style.Border.BorderBottomStyle.Value);
                return _actualBorderBottomColor;
            }
        }

        public RColor ActualBorderLeftColor
        {
            get
            {
                if (_actualBorderLeftColor.IsEmpty)
                    _actualBorderLeftColor = ResolveBorderSideColor(Style.Border.BorderLeftColor, Style.Border.BorderLeftStyle.Value);
                return _actualBorderLeftColor;
            }
        }

        /// <summary>
        /// One border side's <em>used</em> color. <c>currentcolor</c> is deliberately still unresolved
        /// in <see cref="ComputedStyle"/> for the four border longhands - unlike every other color
        /// property, which <c>CssUtils.ApplyCurrentColor</c> substitutes in place during the cascade -
        /// so it is resolved here, per box, against that box's own <c>color</c>.
        /// </summary>
        /// <remarks>
        /// Two reasons it has to be this way round rather than resolved into the cascade like the rest.
        /// <para>
        /// A bevelled side does not resolve to <c>color</c> at all: it resolves to
        /// <see cref="BorderBevelColors.CurrentColorBase"/>, a fixed light grey, everywhere except on a
        /// table display type (issue #1226 - see that field for the measurements). That is a
        /// <em>used</em> value in Blink too, which is why <c>getComputedStyle</c> there still reports
        /// the unsubstituted color.
        /// </para>
        /// <para>
        /// And what a child inherits through <c>border-color: inherit</c> is the computed value, which
        /// is still <c>currentcolor</c> - so the child resolves it against <em>its own</em> color, not
        /// the parent's. Substituting during the cascade broke exactly that: the child inherited the
        /// parent's already-resolved color, so a solid child of a bevelled parent inherited the light
        /// grey and painted a near-invisible border, and before that it inherited the parent's text
        /// color where a browser uses the child's.
        /// </para>
        /// </remarks>
        private RColor ResolveBorderSideColor(string colorValue, LineStyle lineStyle)
        {
            if (!Keywords.CurrentColor.Equals(colorValue, StringComparison.OrdinalIgnoreCase))
                return Owner.GetActualColor(colorValue);

            return BorderBevelColors.IsBeveled(lineStyle) && !IsTableDisplay(ActualDisplay)
                ? BorderBevelColors.CurrentColorBase
                : ActualColor;
        }

        /// <summary>
        /// Whether <paramref name="display"/> is one of css-tables-3's table display types, which Blink
        /// exempts from <see cref="BorderBevelColors.CurrentColorBase"/>. Confirmed against Chrome 153
        /// for all ten: a <c>display: table</c> box with <c>border: 20px inset; color: red</c> paints a
        /// shaded red, while the same declaration on <c>block</c>/<c>inline-block</c>/<c>flex</c>/
        /// <c>grid</c>/<c>list-item</c> paints the two greys. Asked of <see cref="ActualDisplay"/>, not
        /// the cascaded <c>display</c>, because Blink consults a ComputedStyle that CSS Display 3 §2.7
        /// has already blockified - a floated table-cell is a block by then and loses the exemption.
        /// </summary>
        private static bool IsTableDisplay(string display) =>
            display is Keywords.Table or Keywords.InlineTable or Keywords.TableCaption
                or Keywords.TableCell or Keywords.TableColumn or Keywords.TableColumnGroup
                or Keywords.TableFooterGroup or Keywords.TableHeaderGroup or Keywords.TableRow
                or Keywords.TableRowGroup;

        /// <summary>Actual column-rule color (the line drawn between columns in a multi-column container).</summary>
        public RColor ActualColumnRuleColor
        {
            get
            {
                if (_actualColumnRuleColor.IsEmpty) _actualColumnRuleColor = Owner.GetActualColor(Style.MultiColumn.ColumnRuleColor);
                return _actualColumnRuleColor;
            }
        }

        internal void InvalidateBorderTopColor() => _actualBorderTopColor = RColor.Empty;
        internal void InvalidateBorderRightColor() => _actualBorderRightColor = RColor.Empty;
        internal void InvalidateBorderBottomColor() => _actualBorderBottomColor = RColor.Empty;
        internal void InvalidateBorderLeftColor() => _actualBorderLeftColor = RColor.Empty;

        #endregion

        #region Outline

        private double _actualOutlineWidth = double.NaN;
        private RColor _actualOutlineColor = RColor.Empty;
        private double _actualOutlineOffset = double.NaN;

        public double ActualOutlineWidth
        {
            get
            {
                if (!double.IsNaN(_actualOutlineWidth)) return _actualOutlineWidth;

                _actualOutlineWidth = CssValueParser.GetActualBorderWidth(Style.Border.OutlineWidth, Owner);
                if (Style.Border.OutlineStyle.Value == OutlineStyle.None)
                    _actualOutlineWidth = 0f;

                return _actualOutlineWidth;
            }
        }

        /// <summary>
        /// Unaware of the legacy <c>invert</c> keyword by design - <c>OutlineDrawHandler</c> checks the
        /// raw <see cref="CssBox.OutlineColor"/> string for <c>"invert"</c> itself and bypasses this
        /// property entirely in that case, painting via a PDF blend mode instead of a resolved color.
        /// </summary>
        public RColor ActualOutlineColor
        {
            get
            {
                if (_actualOutlineColor.IsEmpty) _actualOutlineColor = Owner.GetActualColor(Style.Border.OutlineColor);
                return _actualOutlineColor;
            }
        }

        public double ActualOutlineOffset
        {
            get
            {
                if (!double.IsNaN(_actualOutlineOffset)) return _actualOutlineOffset;
                _actualOutlineOffset = CssValueParser.ParseLength(Style.Border.OutlineOffset, 0, Owner);
                return _actualOutlineOffset;
            }
        }

        internal void InvalidateOutlineWidth() => _actualOutlineWidth = double.NaN;
        internal void InvalidateOutlineColor() => _actualOutlineColor = RColor.Empty;
        internal void InvalidateOutlineOffset() => _actualOutlineOffset = double.NaN;

        #endregion

        #region Border radii

        private double _actualBorderTopLeftRadiusX = double.NaN;
        private double _actualBorderTopLeftRadiusY = double.NaN;
        private double _actualBorderTopRightRadiusX = double.NaN;
        private double _actualBorderTopRightRadiusY = double.NaN;
        private double _actualBorderBottomRightRadiusX = double.NaN;
        private double _actualBorderBottomRightRadiusY = double.NaN;
        private double _actualBorderBottomLeftRadiusX = double.NaN;
        private double _actualBorderBottomLeftRadiusY = double.NaN;

        public double ActualBorderTopLeftRadiusX
        {
            get
            {
                if (double.IsNaN(_actualBorderTopLeftRadiusX))
                    _actualBorderTopLeftRadiusX = CssValueParser.ParseLength(FirstCssValue(Style.Border.BorderTopLeftRadius), Owner.ActualBoxSizingWidth, Owner);
                return _actualBorderTopLeftRadiusX;
            }
        }

        public double ActualBorderTopLeftRadiusY
        {
            get
            {
                if (double.IsNaN(_actualBorderTopLeftRadiusY))
                    _actualBorderTopLeftRadiusY = CssValueParser.ParseLength(SecondCssValue(Style.Border.BorderTopLeftRadius), Owner.ActualBoxSizingHeight, Owner);
                return _actualBorderTopLeftRadiusY;
            }
        }

        public double ActualBorderTopRightRadiusX
        {
            get
            {
                if (double.IsNaN(_actualBorderTopRightRadiusX))
                    _actualBorderTopRightRadiusX = CssValueParser.ParseLength(FirstCssValue(Style.Border.BorderTopRightRadius), Owner.ActualBoxSizingWidth, Owner);
                return _actualBorderTopRightRadiusX;
            }
        }

        public double ActualBorderTopRightRadiusY
        {
            get
            {
                if (double.IsNaN(_actualBorderTopRightRadiusY))
                    _actualBorderTopRightRadiusY = CssValueParser.ParseLength(SecondCssValue(Style.Border.BorderTopRightRadius), Owner.ActualBoxSizingHeight, Owner);
                return _actualBorderTopRightRadiusY;
            }
        }

        public double ActualBorderBottomRightRadiusX
        {
            get
            {
                if (double.IsNaN(_actualBorderBottomRightRadiusX))
                    _actualBorderBottomRightRadiusX = CssValueParser.ParseLength(FirstCssValue(Style.Border.BorderBottomRightRadius), Owner.ActualBoxSizingWidth, Owner);
                return _actualBorderBottomRightRadiusX;
            }
        }

        public double ActualBorderBottomRightRadiusY
        {
            get
            {
                if (double.IsNaN(_actualBorderBottomRightRadiusY))
                    _actualBorderBottomRightRadiusY = CssValueParser.ParseLength(SecondCssValue(Style.Border.BorderBottomRightRadius), Owner.ActualBoxSizingHeight, Owner);
                return _actualBorderBottomRightRadiusY;
            }
        }

        public double ActualBorderBottomLeftRadiusX
        {
            get
            {
                if (double.IsNaN(_actualBorderBottomLeftRadiusX))
                    _actualBorderBottomLeftRadiusX = CssValueParser.ParseLength(FirstCssValue(Style.Border.BorderBottomLeftRadius), Owner.ActualBoxSizingWidth, Owner);
                return _actualBorderBottomLeftRadiusX;
            }
        }

        public double ActualBorderBottomLeftRadiusY
        {
            get
            {
                if (double.IsNaN(_actualBorderBottomLeftRadiusY))
                    _actualBorderBottomLeftRadiusY = CssValueParser.ParseLength(SecondCssValue(Style.Border.BorderBottomLeftRadius), Owner.ActualBoxSizingHeight, Owner);
                return _actualBorderBottomLeftRadiusY;
            }
        }

        internal void InvalidateBorderTopLeftRadius()
        {
            _actualBorderTopLeftRadiusX = double.NaN;
            _actualBorderTopLeftRadiusY = double.NaN;
        }

        internal void InvalidateBorderTopRightRadius()
        {
            _actualBorderTopRightRadiusX = double.NaN;
            _actualBorderTopRightRadiusY = double.NaN;
        }

        internal void InvalidateBorderBottomRightRadius()
        {
            _actualBorderBottomRightRadiusX = double.NaN;
            _actualBorderBottomRightRadiusY = double.NaN;
        }

        internal void InvalidateBorderBottomLeftRadius()
        {
            _actualBorderBottomLeftRadiusX = double.NaN;
            _actualBorderBottomLeftRadiusY = double.NaN;
        }

        // Returns the first top-level-whitespace-delimited token in a CSS value string (paren-depth-aware,
        // so a calc()/min()/max()/clamp() value's internal spaces aren't mistaken for the delimiter).
        private static string FirstCssValue(string value)
        {
            using var tokens = CssValueParser.SplitTopLevelWhitespace(value).GetEnumerator();
            return tokens.MoveNext() ? tokens.Current : value;
        }

        // Returns the second top-level-whitespace-delimited token, or the first if there is no second
        // (spec: omitted v-radius = h-radius).
        private static string SecondCssValue(string value)
        {
            var tokens = new List<string>(CssValueParser.SplitTopLevelWhitespace(value));
            return tokens.Count > 1 ? tokens[1] : value;
        }

        /// <summary>
        /// Computes overlap-reduced radii for the given rendering rectangle, per the corner-overlap
        /// algorithm in <see href="https://www.w3.org/TR/css-backgrounds-3/#corner-overlap">CSS
        /// Backgrounds and Borders Module Level 3 §4</see>: a single factor f - the minimum, across all
        /// four edges, of that edge's length divided by the sum of the two corner radii on it - is
        /// applied uniformly to every corner's x AND y radius. A per-axis factor (reducing all x radii
        /// by one factor and all y radii by another, independently) is spec-incorrect: for a radius far
        /// more overconstrained on one axis than the other (e.g. `border-radius: 999px` on a short,
        /// wide box), it stretches what should be a circular corner into a near-degenerate ellipse.
        /// </summary>
        internal BorderRadii ComputeRadii(RRect rect)
        {
            double tlX = ActualBorderTopLeftRadiusX, tlY = ActualBorderTopLeftRadiusY;
            double trX = ActualBorderTopRightRadiusX, trY = ActualBorderTopRightRadiusY;
            double brX = ActualBorderBottomRightRadiusX, brY = ActualBorderBottomRightRadiusY;
            double blX = ActualBorderBottomLeftRadiusX, blY = ActualBorderBottomLeftRadiusY;

            return ApplyCornerOverlap(rect, tlX, tlY, trX, trY, brX, brY, blX, blY);
        }

        /// <summary>
        /// Computes overlap-reduced radii for a box's padding edge or content edge, per
        /// <see href="https://www.w3.org/TR/css-backgrounds-3/#corner-clipping">CSS Backgrounds and
        /// Borders Module Level 3 §5.5 (Corner Clipping)</see>: "The padding edge (inner border) radius
        /// is the outer border radius minus the corresponding border thickness. In the case where this
        /// results in a negative value, the inner radius is zero." (For the content edge, the caller
        /// additionally folds the padding widths into <paramref name="insetLeft"/>/etc.)
        /// <para>
        /// This composes with, rather than replaces, the corner-overlap algorithm <see cref="ComputeRadii"/>
        /// already applies: first the box's own declared radii are overlap-reduced against
        /// <paramref name="borderBoxRect"/> to get the "outer" (used, border-box) radius per §4 - the same
        /// value a border-box caller of <see cref="ComputeRadii"/> would get - then each corner's X/Y
        /// component is reduced by the border (and, for the content edge, padding) thickness on its
        /// adjacent edge, clamped to zero. The result is overlap-reduced a second time against
        /// <paramref name="innerRect"/>'s own (smaller) dimensions, since subtracting a constant width can
        /// still leave two adjacent corner radii summing to more than a short inner edge.
        /// </para>
        /// </summary>
        /// <param name="borderBoxRect">the box's own border box, used to resolve the outer (used) radius</param>
        /// <param name="innerRect">the padding-edge or content-edge rectangle the reduced radii apply to</param>
        /// <param name="insetLeft">border-left width (plus padding-left for the content edge)</param>
        /// <param name="insetTop">border-top width (plus padding-top for the content edge)</param>
        /// <param name="insetRight">border-right width (plus padding-right for the content edge)</param>
        /// <param name="insetBottom">border-bottom width (plus padding-bottom for the content edge)</param>
        internal BorderRadii ComputeInnerRadii(RRect borderBoxRect, RRect innerRect,
            double insetLeft, double insetTop, double insetRight, double insetBottom)
        {
            var outer = ComputeRadii(borderBoxRect);

            double tlX = Math.Max(0, outer.TLX - insetLeft), tlY = Math.Max(0, outer.TLY - insetTop);
            double trX = Math.Max(0, outer.TRX - insetRight), trY = Math.Max(0, outer.TRY - insetTop);
            double brX = Math.Max(0, outer.BRX - insetRight), brY = Math.Max(0, outer.BRY - insetBottom);
            double blX = Math.Max(0, outer.BLX - insetLeft), blY = Math.Max(0, outer.BLY - insetBottom);

            return ApplyCornerOverlap(innerRect, tlX, tlY, trX, trY, brX, brY, blX, blY);
        }

        /// <summary>
        /// The corner-overlap algorithm itself (<see href="https://www.w3.org/TR/css-backgrounds-3/#corner-overlap">
        /// CSS Backgrounds and Borders Module Level 3 §4</see>): a single factor f - the minimum, across
        /// all four edges, of that edge's length divided by the sum of the two corner radii on it - is
        /// applied uniformly to every corner's x AND y radius. Shared by <see cref="ComputeRadii"/> (the
        /// box's own declared radii) and <see cref="ComputeInnerRadii"/> (an already inner-reduced set of
        /// radii, re-clamped against the smaller inner rectangle) so the two never derive their own,
        /// possibly-drifting copy of the same reduction. Also called directly by
        /// <see cref="Utils.CssClipPathResolver"/> for an <c>inset(... round &lt;border-radius&gt;)</c>
        /// clip shape's own corner radii, which need the identical reduction against the inset
        /// rectangle but aren't a box's declared <c>border-radius</c> at all.
        /// </summary>
        internal static BorderRadii ApplyCornerOverlap(RRect rect,
            double tlX, double tlY, double trX, double trY,
            double brX, double brY, double blX, double blY)
        {
            double fTop = tlX + trX > 0 && rect.Width > 0 ? rect.Width / (tlX + trX) : 1.0;
            double fBottom = blX + brX > 0 && rect.Width > 0 ? rect.Width / (blX + brX) : 1.0;
            double fLeft = tlY + blY > 0 && rect.Height > 0 ? rect.Height / (tlY + blY) : 1.0;
            double fRight = trY + brY > 0 && rect.Height > 0 ? rect.Height / (trY + brY) : 1.0;

            double f = Math.Min(1.0, Math.Min(Math.Min(fTop, fBottom), Math.Min(fLeft, fRight)));

            return new BorderRadii(tlX * f, tlY * f, trX * f, trY * f,
                brX * f, brY * f, blX * f, blY * f);
        }

        /// <summary>Whether at least one of the box's corners is rounded.</summary>
        public bool IsRounded =>
            ActualBorderTopLeftRadiusX > 0 || ActualBorderTopLeftRadiusY > 0 ||
            ActualBorderTopRightRadiusX > 0 || ActualBorderTopRightRadiusY > 0 ||
            ActualBorderBottomRightRadiusX > 0 || ActualBorderBottomRightRadiusY > 0 ||
            ActualBorderBottomLeftRadiusX > 0 || ActualBorderBottomLeftRadiusY > 0;

        #endregion

        #region Padding, border-spacing

        private double _actualPaddingTop = double.NaN;
        private double _actualPaddingRight = double.NaN;
        private double _actualPaddingBottom = double.NaN;
        private double _actualPaddingLeft = double.NaN;
        private double _actualBorderSpacingHorizontal = double.NaN;
        private double _actualBorderSpacingVertical = double.NaN;

        public double ActualPaddingTop
        {
            get
            {
                if (double.IsNaN(_actualPaddingTop))
                    _actualPaddingTop = CssValueParser.ParseLength(Style.BoxModel.PaddingTop, Owner.Size.Width, Owner);
                return _actualPaddingTop;
            }
        }

        public double ActualPaddingRight
        {
            get
            {
                if (double.IsNaN(_actualPaddingRight))
                    _actualPaddingRight = CssValueParser.ParseLength(Style.BoxModel.PaddingRight, Owner.Size.Width, Owner);
                return _actualPaddingRight;
            }
        }

        public double ActualPaddingBottom
        {
            get
            {
                if (double.IsNaN(_actualPaddingBottom))
                    _actualPaddingBottom = CssValueParser.ParseLength(Style.BoxModel.PaddingBottom, Owner.Size.Width, Owner);
                return _actualPaddingBottom;
            }
        }

        public double ActualPaddingLeft
        {
            get
            {
                if (double.IsNaN(_actualPaddingLeft))
                    _actualPaddingLeft = CssValueParser.ParseLength(Style.BoxModel.PaddingLeft, Owner.Size.Width, Owner);
                return _actualPaddingLeft;
            }
        }

        /// <summary>Actual horizontal border spacing for tables.</summary>
        public double ActualBorderSpacingHorizontal
        {
            get
            {
                if (!double.IsNaN(_actualBorderSpacingHorizontal)) return _actualBorderSpacingHorizontal;

                // Paren-depth-aware split (not a naive regex length-search) so a calc()/min()/max()/clamp()
                // value's internal spaces aren't mistaken for the horizontal/vertical delimiter.
                var parts = new List<string>(CssValueParser.SplitTopLevelWhitespace(Style.Table.BorderSpacing));

                _actualBorderSpacingHorizontal = parts.Count > 0
                    ? CssValueParser.ParseLength(parts[0], 1, Owner)
                    : 0;

                return _actualBorderSpacingHorizontal;
            }
        }

        /// <summary>Actual vertical border spacing for tables.</summary>
        public double ActualBorderSpacingVertical
        {
            get
            {
                if (!double.IsNaN(_actualBorderSpacingVertical)) return _actualBorderSpacingVertical;

                var parts = new List<string>(CssValueParser.SplitTopLevelWhitespace(Style.Table.BorderSpacing));

                _actualBorderSpacingVertical = parts.Count switch
                {
                    0 => 0,
                    1 => CssValueParser.ParseLength(parts[0], 1, Owner),
                    _ => CssValueParser.ParseLength(parts[1], 1, Owner)
                };

                return _actualBorderSpacingVertical;
            }
        }

        internal void InvalidatePaddingTop() => _actualPaddingTop = double.NaN;
        internal void InvalidatePaddingRight() => _actualPaddingRight = double.NaN;
        internal void InvalidatePaddingBottom() => _actualPaddingBottom = double.NaN;
        internal void InvalidatePaddingLeft() => _actualPaddingLeft = double.NaN;

        #endregion

        #region Transform, opacity

        private bool _actualTransformComputed;
        private RMatrix _actualTransformMatrix;
        private Matrix4x4? _actualTransform4;

        /// <summary>
        /// Lazily computes the combined 2D transform matrix for the <c>transform</c>/<c>transform-origin</c>
        /// properties, resolved against this box's own border-box size. Identity when Transform is "none"
        /// or unparsable. 3D transform functions are projected down to a 2D matrix - see CssValueParser.ParseTransform.
        /// </summary>
        public RMatrix ActualTransformMatrix
        {
            get
            {
                if (!_actualTransformComputed)
                {
                    (_actualTransformMatrix, _actualTransform4) = CssValueParser.ParseTransformFull(Style.VisualEffects.Transform, Style.VisualEffects.TransformOrigin, Owner);
                    _actualTransformComputed = true;
                }
                return _actualTransformMatrix;
            }
        }

        /// <summary>
        /// The 4x4 the 2D matrix above was projected from (transform origin baked in, box-local), or null when the box has no
        /// <c>transform</c>. Its z=0 restriction is a homography when it involves <c>perspective()</c> or a perspective the parent applies.
        /// </summary>
        public Matrix4x4? ActualTransform4
        {
            get
            {
                _ = ActualTransformMatrix;
                return _actualTransform4;
            }
        }

        /// <summary>True when this box has a non-identity CSS transform to apply at paint time.</summary>
        public bool IsTransformed => !ActualTransformMatrix.IsIdentity;

        private double _actualPerspective = double.NaN;

        /// <summary>The <c>perspective</c> distance this box gives its children, in layout units; 0 for <c>none</c>.</summary>
        public double ActualPerspective
        {
            get
            {
                if (double.IsNaN(_actualPerspective))
                {
                    var value = Style.VisualEffects.Perspective;
                    _actualPerspective = string.IsNullOrWhiteSpace(value) || value.Trim().Equals(Keywords.None, StringComparison.OrdinalIgnoreCase)
                        ? 0
                        : Math.Max(0, CssValueParser.ParseLength(value, 0, Owner));
                }

                return _actualPerspective;
            }
        }

        /// <summary>The <c>perspective-origin</c> (the vanishing point), relative to this box's border box, in layout units.</summary>
        public (double X, double Y) ActualPerspectiveOrigin
        {
            get
            {
                var (x, y, _) = CssValueParser.ParseTransformOriginPublic(Style.VisualEffects.PerspectiveOrigin, Owner);
                return (x, y);
            }
        }

        /// <summary>True when <c>backface-visibility: hidden</c>: the box is not painted while it faces away from the viewer.</summary>
        public bool IsBackfaceHidden =>
            string.Equals(Style.VisualEffects.BackfaceVisibility?.Trim(), Keywords.Hidden, StringComparison.OrdinalIgnoreCase);

        /// <summary>True when the computed <c>transform-style</c> is <c>preserve-3d</c>. The <em>used</em> value also depends on the grouping properties that force <c>flat</c>: see <c>DomUtils.EstablishesPreserve3d</c>.</summary>
        public bool IsPreserve3dRequested =>
            string.Equals(Style.VisualEffects.TransformStyle?.Trim(), Keywords.Preserve3d, StringComparison.OrdinalIgnoreCase);

        internal void InvalidateTransform() => _actualTransformComputed = false;

        internal void InvalidatePerspective() => _actualPerspective = double.NaN;

        private bool _actualOpacityComputed;
        private double _actualOpacity;

        /// <summary>
        /// Lazily computes the used value of the <c>opacity</c> property, clamped to [0, 1].
        /// An empty/unparsable value (or the initial "1") resolves to fully opaque.
        /// </summary>
        public double ActualOpacity
        {
            get
            {
                if (!_actualOpacityComputed)
                {
                    _actualOpacity = string.IsNullOrEmpty(Style.VisualEffects.Opacity)
                        ? 1.0
                        : Math.Clamp(CssValueParser.ParseNumber(Style.VisualEffects.Opacity, 1.0), 0.0, 1.0);
                    _actualOpacityComputed = true;
                }
                return _actualOpacity;
            }
        }

        /// <summary>True when this box's <c>opacity</c> is fully opaque - false when a group-opacity transparency-group composite is needed at paint time.</summary>
        public bool IsOpaque => ActualOpacity >= 1.0;

        internal void InvalidateOpacity() => _actualOpacityComputed = false;

        private bool _filterFunctionsComputed;
        private List<FilterGrammar.FilterFunction> _filterFunctions = [];

        /// <summary>
        /// Lazily parses the used value of the <c>filter</c> property (Filter Effects Level 1 §3) into its
        /// ordered function list - empty for <c>none</c> or an unparsable value. Kept as the raw
        /// <see cref="FilterGrammar.FilterFunction"/> list (never pre-resolved
        /// <see cref="Adapters.Entities.ColorMatrix"/>es) for the same reason <see cref="BoxShadowGrammar"/>'s
        /// own layers stay raw text: <c>drop-shadow()</c>'s lengths still need box-relative resolution via
        /// <c>CssValueParser.ParseLength</c> against THIS box, which only the paint-time caller
        /// (<c>FragmentPainter.PaintFilterDropShadows</c>) can do.
        /// </summary>
        public IReadOnlyList<FilterGrammar.FilterFunction> ActualFilterFunctions
        {
            get
            {
                if (!_filterFunctionsComputed)
                {
                    using (var pooledTokens = CssValueParser.GetCssTokensPooled(Style.VisualEffects.Filter))
                    {
                        List<Token> tokens = pooledTokens;
                        _filterFunctions = FilterGrammar.TryParse(tokens) ?? [];
                    }
                    _filterFunctionsComputed = true;
                }
                return _filterFunctions;
            }
        }

        internal void InvalidateFilter() => _filterFunctionsComputed = false;

        private bool _backdropFilterFunctionsComputed;
        private List<FilterGrammar.FilterFunction> _backdropFilterFunctions = [];

        /// <summary>
        /// Lazily parses the used value of <c>backdrop-filter</c> (Filter Effects Level 2 §3.1) into its ordered function list -
        /// empty for <c>none</c> or an unparsable value. The grammar is <c>filter</c>'s own.
        /// </summary>
        public IReadOnlyList<FilterGrammar.FilterFunction> ActualBackdropFilterFunctions
        {
            get
            {
                if (!_backdropFilterFunctionsComputed)
                {
                    using (var pooledTokens = CssValueParser.GetCssTokensPooled(Style.VisualEffects.BackdropFilter))
                    {
                        List<Token> tokens = pooledTokens;
                        _backdropFilterFunctions = FilterGrammar.TryParse(tokens) ?? [];
                    }

                    _backdropFilterFunctionsComputed = true;
                }

                return _backdropFilterFunctions;
            }
        }

        internal void InvalidateBackdropFilter() => _backdropFilterFunctionsComputed = false;

        private bool _actualMixBlendModeComputed;
        private BlendMode _actualMixBlendMode;

        /// <summary>
        /// The used value of <c>mix-blend-mode</c>. <see cref="CssProperty{T}.Value"/> is
        /// <see cref="BlendMode"/>, not <c>BlendMode?</c> - <c>CssProperty&lt;T&gt;</c>'s own <c>T?</c> is
        /// erased to plain <c>T</c> for an unconstrained generic parameter (a real C# generics quirk: only
        /// a <c>where T : struct</c> constraint would make it genuinely <c>Nullable&lt;T&gt;</c>), so the
        /// CSS-wide-keyword/unresolved-<c>var()</c> case (where no real value was ever parsed) reads back
        /// as <c>default(BlendMode)</c> - which is <see cref="BlendMode.Normal"/>, since it's declared
        /// first - already the correct fail-open fallback with no explicit handling needed here.
        /// </summary>
        public BlendMode ActualMixBlendMode
        {
            get
            {
                if (!_actualMixBlendModeComputed)
                {
                    _actualMixBlendMode = Style.VisualEffects.MixBlendMode.Value;
                    _actualMixBlendModeComputed = true;
                }
                return _actualMixBlendMode;
            }
        }

        #endregion

        #region Word/letter spacing, text-indent

        /// <summary>The width of whitespace between words.</summary>
        private double _actualWordSpacing = double.NaN;

        /// <summary>The extra space added between each pair of adjacent characters.</summary>
        private double _actualLetterSpacing = double.NaN;

        private double _actualTextIndent = double.NaN;
        private bool _actualTextIndentHanging;
        private bool _actualTextIndentEachLine;

        public double ActualWordSpacing => _actualWordSpacing;
        public double ActualLetterSpacing => _actualLetterSpacing;

        /// <summary>Measures the width of whitespace between words (populates <see cref="ActualWordSpacing"/>).</summary>
        internal void MeasureWordSpacing(RGraphics g)
        {
            if (!double.IsNaN(ActualWordSpacing)) return;

            // CssUtils.WhiteSpace already adds the declared word-spacing length itself (on top of the
            // whitespace glyph's own width) when WordSpacing isn't "normal" - a second addition here
            // used to double-count the declared value on top of that.
            _actualWordSpacing = CssUtils.WhiteSpace(g, Owner);
        }

        /// <summary>
        /// Measures the extra space added between each pair of adjacent characters (populates
        /// <see cref="ActualLetterSpacing"/>). Unlike <see cref="MeasureWordSpacing"/>, there's no
        /// whitespace-glyph base width to add to - the base is always 0 for <c>normal</c>, so this
        /// needs no <see cref="RGraphics"/>/font-metric input.
        /// </summary>
        internal void MeasureLetterSpacing()
        {
            if (!double.IsNaN(ActualLetterSpacing)) return;

            _actualLetterSpacing = Style.Text.LetterSpacing.Value is { IsValue: true, Value: { } letterSpacing }
                ? CssValueParser.ParseLength(letterSpacing, 1, Owner)
                : 0;
        }

        /// <summary>
        /// Resolves <c>tab-size</c>'s (<see href="https://www.w3.org/TR/css-text-4/#tab-size-property">CSS
        /// Text 4 §3.6</see>) <c>&lt;number&gt; | &lt;length&gt;</c> grammar against <see cref="Owner"/>'s
        /// own raw cascaded string. Unlike <c>line-height</c>'s bare-number grammar (which multiplies the
        /// element's own font-size), a bare number here is a multiple of the *space glyph's* advance
        /// width - only known once the preserved tab's actual font is resolved - so this stops short of a
        /// final point value for that case: <c>IsNumber</c> true means <c>Value</c> is that raw
        /// multiplier, for <see cref="CssBox.MeasureWordsSize"/> (and its first-line-style siblings) to
        /// scale by the word's own measured space width; <c>IsNumber</c> false means <c>Value</c> is
        /// already a resolved absolute tab-stop width in points. <see cref="Owner"/>'s
        /// <see cref="CssBox.TabSize"/> is only ever set through the cascade's own customValidator
        /// (css-properties.json's "tab-size" entry), which already re-checks this exact grammar, so
        /// <see cref="CssValueParser.TryParseLengthOrUnitless"/> parsing it here is not re-validated.
        /// </summary>
        internal (bool IsNumber, double Value) ResolvedTabSize
        {
            get
            {
                CssValueParser.TryParseLengthOrUnitless(Owner.TabSize, out var parsed);
                return parsed.IsUnitless
                    ? (true, parsed.Unitless!.Value)
                    : (false, CssValueParser.ParseLength(parsed.LengthOrCalc!.Value, 0, Owner));
            }
        }

        /// <summary>The length/percentage component of <c>text-indent</c>, resolved to layout units. Which
        /// line(s) it applies to depends on <see cref="ActualTextIndentHanging"/>/
        /// <see cref="ActualTextIndentEachLine"/> - see <c>CssLayoutEngine.GetLineTextIndent</c>.</summary>
        public double ActualTextIndent
        {
            get
            {
                EnsureTextIndentResolved();
                return _actualTextIndent;
            }
        }

        /// <summary>Whether <c>text-indent</c>'s <c>hanging</c> keyword was specified (CSS Text 3 §3).</summary>
        public bool ActualTextIndentHanging
        {
            get
            {
                EnsureTextIndentResolved();
                return _actualTextIndentHanging;
            }
        }

        /// <summary>Whether <c>text-indent</c>'s <c>each-line</c> keyword was specified (CSS Text 3 §3).</summary>
        public bool ActualTextIndentEachLine
        {
            get
            {
                EnsureTextIndentResolved();
                return _actualTextIndentEachLine;
            }
        }

        /// <summary>Resolves and caches <see cref="ActualTextIndent"/>/<see cref="ActualTextIndentHanging"/>/
        /// <see cref="ActualTextIndentEachLine"/> together, since they all come from one parse of the same
        /// <c>text-indent</c> value (<see cref="TextIndentGrammar"/>, shared with the CSS-OM layer that
        /// validated it at parse time).</summary>
        private void EnsureTextIndentResolved()
        {
            if (!double.IsNaN(_actualTextIndent)) return;

            using var pooledTokens = CssValueParser.GetCssTokensPooled(Style.Text.TextIndent);
            List<Token> tokens = pooledTokens;
            if (TextIndentGrammar.TryParse(tokens, out var length, out var hasHanging, out var hasEachLine))
            {
                _actualTextIndent = CssValueParser.ParseLength(length.Text, Owner.Size.Width, Owner);
                _actualTextIndentHanging = hasHanging;
                _actualTextIndentEachLine = hasEachLine;
            }
            else
            {
                _actualTextIndent = 0;
            }
        }

        #endregion

        #region Color, background-color

        private RColor _actualColor = RColor.Empty;
        private RColor _actualBackgroundColor = RColor.Empty;

        public RColor ActualColor
        {
            get
            {
                if (_actualColor.IsEmpty) _actualColor = Owner.GetActualColor(Style.Text.Color);
                return _actualColor;
            }
        }

        public RColor ActualBackgroundColor
        {
            get
            {
                if (_actualBackgroundColor.IsEmpty) _actualBackgroundColor = Owner.GetActualColor(Style.Background.BackgroundColor);
                return _actualBackgroundColor;
            }
        }

        internal void InvalidateColor() => _actualColor = RColor.Empty;

        #endregion

        #region Text alignment

        private HorizontalAlignment? _actualTextAlignAll;

        /// <summary>
        /// This box's own <see cref="TextArea.TextAlignAll"/> (css-text-3 §6.2), with <c>match-parent</c>
        /// fully resolved: an inherited <c>start</c>/<c>end</c> that <c>match-parent</c> asks to be
        /// interpreted against the <i>parent's</i> own <c>direction</c> is resolved right here, against
        /// <see cref="CssBox.ParentBox"/>'s direction - not <see cref="Owner"/>'s own, and not deferred to
        /// whichever box's <c>CssLayoutEngine.ApplyHorizontalAlignment</c> eventually reads this value.
        /// <c>Left</c>/<c>Right</c>/<c>Center</c>/<c>Justify</c> pass through unchanged; a plain
        /// (non-<c>match-parent</c>) <c>Start</c>/<c>End</c> stays symbolic - <c>CssLayoutEngine</c> still
        /// resolves those against <see cref="Owner"/>'s own direction, unchanged. The root element's
        /// <c>match-parent</c> computes to <c>Start</c> (there being no parent to consult), per spec.
        /// Recursive like <see cref="ActualNumericWeight"/>'s own parent walk - cached with no
        /// invalidation-on-every-ancestor-mutation for the same reason that one needs none: both are only
        /// ever read after the cascade has finished assigning every box's own properties.
        /// </summary>
        public HorizontalAlignment ActualTextAlignAll => _actualTextAlignAll ??= ComputeTextAlignAll();

        private HorizontalAlignment ComputeTextAlignAll()
        {
            var value = Style.Text.TextAlignAll.Value;
            if (value != HorizontalAlignment.MatchParent) return value;

            var parent = Owner.ParentBox;
            if (parent is null) return HorizontalAlignment.Start;

            return parent.ActualTextAlignAll switch
            {
                HorizontalAlignment.Start => parent.Direction.Value == DirectionMode.Rtl ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                HorizontalAlignment.End => parent.Direction.Value == DirectionMode.Rtl ? HorizontalAlignment.Left : HorizontalAlignment.Right,
                var other => other
            };
        }

        internal void InvalidateTextAlignAll() => _actualTextAlignAll = null;

        private TextAlignLast? _actualTextAlignLast;

        /// <summary>
        /// This box's own <see cref="TextArea.TextAlignLast"/> (css-text-3 §6.3), with <c>match-parent</c>
        /// fully resolved the same way as <see cref="ActualTextAlignAll"/> - see its own doc comment for
        /// the parent-direction reasoning. A parent whose own resolved value is <c>Auto</c> (it never
        /// declared its own <c>text-align-last</c>) passes <c>Auto</c> straight through the <c>other</c>
        /// arm below, so <c>match-parent</c> degrades to <c>auto</c>'s existing "defer to
        /// <c>text-align-all</c>" behavior with no special-casing needed.
        /// </summary>
        public TextAlignLast ActualTextAlignLast => _actualTextAlignLast ??= ComputeTextAlignLast();

        private TextAlignLast ComputeTextAlignLast()
        {
            var value = Style.Text.TextAlignLast.Value;
            if (value != TextAlignLast.MatchParent) return value;

            var parent = Owner.ParentBox;
            if (parent is null) return TextAlignLast.Start;

            return parent.ActualTextAlignLast switch
            {
                TextAlignLast.Start => parent.Direction.Value == DirectionMode.Rtl ? TextAlignLast.Right : TextAlignLast.Left,
                TextAlignLast.End => parent.Direction.Value == DirectionMode.Rtl ? TextAlignLast.Left : TextAlignLast.Right,
                var other => other
            };
        }

        internal void InvalidateTextAlignLast() => _actualTextAlignLast = null;

        #endregion

        #region Font palette

        private RFontPalette? _actualFontPalette;
        private bool _actualFontPaletteResolved;

        /// <summary>
        /// The resolved <c>font-palette</c> selection for this box's used font (CSS Fonts 4), or null for
        /// the default palette. Only meaningful for a COLR/CPAL color font.
        /// </summary>
        public RFontPalette? ActualFontPalette
        {
            get
            {
                if (!_actualFontPaletteResolved)
                {
                    _actualFontPalette = FontPaletteResolver.Resolve(Style.Font.FontPalette, ActualFont, Style.Font.FontFamily, Owner.FontPaletteValuesRegistry);
                    _actualFontPaletteResolved = true;
                }

                return _actualFontPalette;
            }
        }

        internal void InvalidateFontPalette()
        {
            _actualFontPaletteResolved = false;
            _actualFontPalette = null;
        }

        private LigatureFeatures? _actualFontVariantLigatures;

        /// <summary>
        /// The resolved GSUB ligature features (see <see cref="LigatureFeatures"/>) for this box's
        /// text, from the CSS <c>font-variant-ligatures</c> value - the common-ligatures,
        /// discretionary, historical, and contextual (<c>calt</c>, via GSUB Lookup Types 5/6) axes
        /// all independently change shaping. Per the CSS Fonts spec, required ligatures (<c>rlig</c>)
        /// are never affected by this property - not even by <c>none</c> - so the
        /// common-ligatures-off case still resolves to <see cref="LigatureFeatures.Required"/> rather
        /// than <see cref="LigatureFeatures.None"/>; likewise <c>no-common-ligatures</c> alone (without
        /// a separate <c>no-contextual</c>) leaves contextual alternates on, and vice versa - each
        /// axis is resolved independently rather than as one all-or-nothing default.
        /// </summary>
        public LigatureFeatures ActualFontVariantLigatures =>
            _actualFontVariantLigatures ??= TextShapingFeatureResolver.ResolveLigatures(Style.Font.FontVariantLigatures);

        private FontVariantCapsFeature? _actualFontVariantCaps;

        /// <summary>
        /// The caps feature that should actually be requested from the shaping layer for this box's
        /// text: <see cref="FontVariantCapsFeature.None"/> for <c>normal</c>, for a keyword the
        /// resolved font lacks full GSUB support for (see <see cref="RFont.SupportsFontVariantCaps"/>),
        /// or - for small-caps/all-small-caps specifically - whenever <c>CssBox.AddWord</c> is instead
        /// synthesizing the effect (real substitution must never also be requested in that case).
        /// </summary>
        public FontVariantCapsFeature ActualFontVariantCaps
        {
            get
            {
                if (_actualFontVariantCaps is { } cached) return cached;

                var requested = TextShapingFeatureResolver.ResolveCapsRequested(Style.Font.FontVariantCaps);

                var resolved = requested != FontVariantCapsFeature.None && ActualFont.SupportsFontVariantCaps(requested)
                    ? requested
                    : FontVariantCapsFeature.None;

                _actualFontVariantCaps = resolved;
                return resolved;
            }
        }

        private FontVariantPositionFeature? _requestedFontVariantPosition;
        private FontVariantPositionFeature? _actualFontVariantPosition;

        /// <summary>
        /// The <c>font-variant-position</c> keyword this box's style asks for, before any capability
        /// gating - <see cref="FontVariantPositionFeature.None"/> only for <c>normal</c>. This is the
        /// question "should this text be a sub/superscript at all", which stays true whether the font
        /// has real <c>subs</c>/<c>sups</c> glyphs or the effect has to be synthesized.
        /// </summary>
        public FontVariantPositionFeature RequestedFontVariantPosition =>
            _requestedFontVariantPosition ??= TextShapingFeatureResolver.ResolvePositionRequested(Style.Font.FontVariantPosition);

        /// <summary>
        /// The position feature that should actually be requested from the shaping layer:
        /// <see cref="RequestedFontVariantPosition"/> when the resolved font really has the matching
        /// GSUB feature, and <see cref="FontVariantPositionFeature.None"/> otherwise - in which case
        /// <c>CssBox.AddWord</c> synthesizes the sub/superscript instead, and real substitution must
        /// never also be requested. CSS Fonts 4 makes this an all-or-nothing choice per run ("if one
        /// such glyph is not available for a character, all the characters in that run are rendered
        /// using synthesized glyphs"), which is exactly what a single per-box capability answer gives.
        /// </summary>
        public FontVariantPositionFeature ActualFontVariantPosition
        {
            get
            {
                if (_actualFontVariantPosition is { } cached) return cached;

                var requested = RequestedFontVariantPosition;

                var resolved = requested != FontVariantPositionFeature.None && ActualFont.SupportsFontVariantPosition(requested)
                    ? requested
                    : FontVariantPositionFeature.None;

                _actualFontVariantPosition = resolved;
                return resolved;
            }
        }

        private NumericFeatures? _actualFontVariantNumeric;

        /// <summary>The resolved GSUB numeric features (CSS <c>font-variant-numeric</c>) for this box's
        /// text - no capability gating (unlike caps): a tag the resolved font lacks simply activates no
        /// lookup and is silently inert.</summary>
        public NumericFeatures ActualFontVariantNumeric =>
            _actualFontVariantNumeric ??= TextShapingFeatureResolver.ResolveNumeric(Style.Font.FontVariantNumeric);

        private EastAsianFeatures? _actualFontVariantEastAsian;

        /// <summary>The resolved GSUB east-asian features (CSS <c>font-variant-east-asian</c>) for this
        /// box's text - no capability gating, same rationale as <see cref="ActualFontVariantNumeric"/>.</summary>
        public EastAsianFeatures ActualFontVariantEastAsian =>
            _actualFontVariantEastAsian ??= TextShapingFeatureResolver.ResolveEastAsian(Style.Font.FontVariantEastAsian);

        private IReadOnlyList<(string Tag, int Value)>? _actualFontFeatureSettings;

        /// <summary>
        /// The resolved explicit OpenType feature tags (CSS <c>font-feature-settings</c>) for this
        /// box's text, parsed from the cascaded string (e.g. <c>"smcp" 1, "onum" 1</c>) into
        /// (tag, value) pairs - <c>on</c>/<c>off</c> resolve to 1/0, a bare tag with no value defaults
        /// to 1. <c>normal</c> resolves to an empty list.
        /// </summary>
        public IReadOnlyList<(string Tag, int Value)> ActualFontFeatureSettings =>
            _actualFontFeatureSettings ??= TextShapingFeatureResolver.ResolveFeatureSettings(Style.Font.FontFeatureSettings);

        private IReadOnlyList<(string Tag, int Value)>? _actualFontVariantAlternates;

        /// <summary>
        /// The resolved OpenType feature tags (CSS <c>font-variant-alternates</c>) for this box's text -
        /// each clause's ident argument(s) looked up against the document's <c>@font-feature-values</c>
        /// registry for this box's used font family and turned into <c>(tag, value)</c> pairs (see
        /// <see cref="FontVariantAlternatesResolver"/>). An unmatched name is inert.
        /// </summary>
        public IReadOnlyList<(string Tag, int Value)> ActualFontVariantAlternates =>
            _actualFontVariantAlternates ??= FontVariantAlternatesResolver.Resolve(
                Style.Font.FontVariantAlternates, Style.Font.FontFamily, Owner.FontFeatureValuesRegistry);

        private bool? _actualFontKerning;

        /// <summary>
        /// The resolved CSS <c>font-kerning</c> value: <c>false</c> only for <c>none</c> - both
        /// <c>auto</c> (the initial value) and <c>normal</c> mean "apply GPOS kerning when the font
        /// and script support it," matching real browser behavior for <c>auto</c>'s UA-discretion
        /// wording. Gates GPOS Lookup Types 1/2 (<c>kern</c>) only - mark-to-base/mark-to-mark
        /// positioning (<c>mark</c>/<c>mkmk</c>) is requested unconditionally by
        /// <see cref="PeachPDF.Text.GposPositioner"/>, since combining-mark attachment isn't a
        /// stylistic opt-out the way kerning is.
        /// </summary>
        public bool ActualFontKerning =>
            _actualFontKerning ??= TextShapingFeatureResolver.ResolveKerning(Style.Font.FontKerning);

        private TextShapingFeatures? _actualTextShapingFeatures;

        /// <summary>
        /// The single combined GSUB feature request for this box's text - ligatures, caps, numeric,
        /// east-asian, position, and explicit <c>font-feature-settings</c>/<c>font-variant-alternates</c>
        /// tags all folded into one <see cref="TextShapingFeatures"/> value, the one actually threaded
        /// into every measure/paint call site.
        /// </summary>
        public TextShapingFeatures ActualTextShapingFeatures
        {
            get
            {
                if (_actualTextShapingFeatures is { } cached) return cached;

                var resolved = new TextShapingFeatures(
                    ActualFontVariantLigatures,
                    ActualFontVariantCaps,
                    ActualFontVariantNumeric,
                    ActualFontVariantEastAsian,
                    MergeExplicitFeatures(ActualFontFeatureSettings, ActualFontVariantAlternates),
                    Kerning: ActualFontKerning,
                    Language: Owner.Language,
                    Position: ActualFontVariantPosition);

                _actualTextShapingFeatures = resolved;
                return resolved;
            }
        }

        // Combines font-feature-settings and font-variant-alternates into one tag-deduplicated list, an
        // alternates entry always replacing a feature-settings entry for the same tag (CSS Fonts 4 §6.8:
        // "a value in font-variant-alternates takes precedence over the value in font-feature-settings")
        // - done here, as a merge step, rather than by adding the open-ended ssNN/cvNN/salt/swsh/ornm/nalt/
        // hist tag set to GsubShaper's own fixed ReservedTags (which only covers the closed enum-backed
        // tag sets the other font-variant-* longhands produce). Deduplicating here also sidesteps a subtler
        // ordering hazard in GsubShaper.GetActiveLookupIndices: a bare concatenation would let a
        // feature-settings entry with value >= 2 (routed through its own customAltIndexByTag pass, which
        // always overwrites last) silently outlast a same-tag alternates entry with value 1 (routed through
        // defaultTags), regardless of which one was actually appended later.
        private static IReadOnlyList<(string Tag, int Value)> MergeExplicitFeatures(
            IReadOnlyList<(string Tag, int Value)> featureSettings, IReadOnlyList<(string Tag, int Value)> alternates)
        {
            if (alternates.Count == 0) return featureSettings;
            if (featureSettings.Count == 0) return alternates;

            var merged = new Dictionary<string, int>(featureSettings.Count + alternates.Count);
            foreach (var (tag, value) in featureSettings) merged[tag] = value;
            foreach (var (tag, value) in alternates) merged[tag] = value;

            var result = new List<(string, int)>(merged.Count);
            foreach (var pair in merged) result.Add((pair.Key, pair.Value));
            return result;
        }

        #endregion

        #region Font

        private RFont? _actualFont;

        /// <summary>The font that should be actually used to paint the text of the box.</summary>
        public RFont ActualFont
        {
            get
            {
                if (_actualFont != null) return _actualFont;

                if (string.IsNullOrEmpty(Style.Font.FontFamily))
                {
                    Owner.FontFamily = DefaultFontResolver.DefaultFont;
                }

                // Defensive: FontArea's own default already seeds FontSize to "medium" (a real Keyword),
                // so this is unreachable via any box built from ComputedStyle.Default in the normal way -
                // kept as a fail-safe for a box whose ComputedStyle was otherwise assembled without going
                // through that default, mirroring the FontFamily check just above.
                if (Style.Font.FontSize.Value is { IsKeyword: false, IsValue: false })
                {
                    Owner.FontSize = CssKeywordOrValueParser.FromCssText<FontSizeKeyword, LengthOrCalc>(
                        DefaultFontResolver.FontSize.ToString(CultureInfo.InvariantCulture) + "pt",
                        Map.FontSizeKeywords, CssValueParser.TryParseLengthOrCalc, FontSizeKeyword.Medium);
                }

                var st = GetActualFontStyleFlags();

                double parentSize = DefaultFontResolver.FontSize;
                double remSize;

                var parentBox = Owner.ParentBox;

                if (parentBox is not null)
                {
                    // parentBox.ActualFont.Size (like GetRemHeight()'s own ActualFont.Size read below) is
                    // in the adapter's device-scaled font-measurement space - CreateFontInt divides a
                    // requested size by PixelsPerPoint once to get there. FontSizeResolver.Resolve expects
                    // its parentSize/remSize inputs in true CSS points (the same space Style.Font.FontSize
                    // itself is authored in), so the device scaling has to be undone here before handing
                    // it in - otherwise a relative font-size (em/%/smaller/larger) resolves against a
                    // reference that's already off by PixelsPerPoint, then gets divided by PixelsPerPoint
                    // again when its own font is created, compounding into a value that's wrong by roughly
                    // PixelsPerPoint² instead of exactly right. PixelsPerPoint is usually 1.0 (so this was
                    // invisible) but a non-default PixelsPerInch, or ShrinkToFit/ScaleToPageSize, both
                    // legitimately move it away from 1.0 - verified directly by generating PDFs at a
                    // non-default PixelsPerInch and confirming the stored/computed font size lands on the
                    // spec-correct value (see FontSizeInheritanceIntegrationTests.cs, and this fix's own
                    // commit message for the reasoning).
                    var pixelsPerPoint = (Owner.HtmlContainer?.Adapter as PdfSharpAdapter)?.PixelsPerPoint ?? 1.0;
                    parentSize = parentBox.ActualFont.Size * pixelsPerPoint;
                    remSize = GetRemHeight() * pixelsPerPoint;
                }
                else
                {
                    remSize = DefaultFontResolver.FontSize;
                }

                // For a box with text content this basis is unreachable in practice - word-splitting
                // (DomParser.CorrectTextBoxes, during cascade) reads ActualFont before any layout pass
                // exists, caching this size permanently against a container that hasn't been laid out yet.
                // Correct only for a box whose ActualFont happens to first be read post-layout - see
                // .claude/accepted-gaps/font-size-container-relative-units-resolve-to-zero-for-text-content.md.
                var (containerWidthPt, containerHeightPt, containerInlinePt, containerBlockPt) = Owner.GetContainerRelativeUnitBasis();
                var (viewportWidthPt, viewportHeightPt, viewportInlinePt, viewportBlockPt) = Owner.GetViewportUnitBasis();
                // A font-size's ex/ch/cap/ic/lh refer to the PARENT's font and its root-element variants to the
                // root's (CSS Values 4 §6.1.1). By this point only a calc() or a root-element variant can
                // still be font-relative (the rest were resolved eagerly - see ResolveFontSizeValueComputation),
                // so the scoping wrapper is only allocated for those.
                IFontMetricSource? sizeFonts = Style.Font.FontSize.Value.Value is { } sizeValue
                                               && (sizeValue.IsCalc || sizeValue.Length!.Value.IsFontRelative)
                    ? new ParentScopedFontMetrics(parentBox?.DerivedStyle, this)
                    : null;
                var fsize = FontSizeResolver.Resolve(Style.Font.FontSize.Value, parentSize, remSize,
                    containerInlinePt, containerBlockPt, viewportWidthPt, viewportHeightPt,
                    containerWidthPt, containerHeightPt, viewportInlinePt, viewportBlockPt, sizeFonts);

                _actualFont = Owner.GetCachedFont(Style.Font.FontFamily!, fsize, st, ActualNumericWeight, ActualStretch, ActualObliqueSkewSinus)
                              ?? Owner.GetCachedFont(DefaultFontResolver.DefaultFont, fsize, st, ActualNumericWeight, ActualStretch, ActualObliqueSkewSinus);

                if (_actualFont is null)
                {
                    throw new HtmlRenderException($"Cannot find font: {Style.Font.FontFamily} and Default Font {DefaultFontResolver.DefaultFont} is not installed", HtmlRenderErrorType.General);
                }

                return _actualFont!;
            }
        }

        /// <summary>
        /// Resolves a font with this box's own family/style/weight/stretch/oblique (the same inputs
        /// <see cref="ActualFont"/> uses) but an explicit point size instead of the box's cascaded
        /// <c>font-size</c> - for a caller that already computed its own target size out-of-band (e.g.
        /// an interactive PDF form field's "auto font size" fit-to-height appearance stream) and needs
        /// the box's real font identity at that size, not a re-derivation of what size to use.
        /// </summary>
        internal RFont GetActualFontAtSize(double fsize)
        {
            var st = GetActualFontStyleFlags();
            return Owner.GetCachedFont(Style.Font.FontFamily!, fsize, st, ActualNumericWeight, ActualStretch, ActualObliqueSkewSinus)
                   ?? Owner.GetCachedFont(DefaultFontResolver.DefaultFont, fsize, st, ActualNumericWeight, ActualStretch, ActualObliqueSkewSinus)
                   ?? throw new HtmlRenderException($"Cannot find font: {Style.Font.FontFamily} and Default Font {DefaultFontResolver.DefaultFont} is not installed", HtmlRenderErrorType.General);
        }

        /// <summary>
        /// This box's resolved <c>font-size</c>, in the same units as <see cref="ActualFont"/>'s own
        /// <c>Size</c> - a thin accessor kept alongside it for symmetry with the rest of this class's
        /// naming, not an independent computation. <see cref="CssBox.FontSize"/> is safe to read as a plain
        /// cascaded value everywhere else (the CSS-OM, tests asserting the authored value) because every
        /// parent-relative form is eagerly resolved to an absolute point value in its setter - see that
        /// setter's own doc comment.
        /// </summary>
        public double ActualFontSize => ActualFont.Size;

        private int? _actualNumericWeight;

        /// <summary>
        /// This box's own <see cref="FontArea.FontWeight"/>, resolved to a concrete CSS Fonts numeric
        /// weight (1-1000) via <see cref="FontWeightResolver"/> - <c>bolder</c>/<c>lighter</c> are stepped
        /// relative to the parent's own resolved weight, not treated as a fixed "always bold"/"always
        /// normal". Cached like <see cref="ActualFont"/> - both are only ever read after the cascade has
        /// finished assigning every box's own properties, so there's no need to invalidate this when
        /// <see cref="FontArea.FontWeight"/> is set.
        /// </summary>
        public int ActualNumericWeight
        {
            get
            {
                if (_actualNumericWeight is { } cached) return cached;

                var parentWeight = Owner.ParentBox is { } parent ? parent.ActualNumericWeight : 400;
                var resolved = FontWeightResolver.Resolve(Style.Font.FontWeight.Value, parentWeight);
                _actualNumericWeight = resolved;
                return resolved;
            }
        }

        private int? _actualStretch;

        /// <summary>
        /// This box's own <see cref="FontArea.FontStretch"/> keyword, resolved to a concrete CSS
        /// Fonts numeric stretch (1-9, matching OS/2 <c>usWidthClass</c>) via <see cref="FontStretchResolver"/>.
        /// Unlike <see cref="ActualNumericWeight"/>, <c>font-stretch</c> has no parent-relative keywords, so
        /// this doesn't need to walk up the box tree.
        /// </summary>
        public int ActualStretch
        {
            get
            {
                if (_actualStretch is { } cached) return cached;

                var resolved = FontStretchResolver.Resolve(Style.Font.FontStretch.Value);
                _actualStretch = resolved;
                return resolved;
            }
        }

        /// <summary>
        /// This box's own <see cref="FontArea.FontStyle"/>, resolved to a faux-italic skew factor (the
        /// sine of the declared angle) when it's the CSS Fonts Level 4 <c>oblique &lt;angle&gt;</c> form -
        /// null for <c>italic</c>, bare <c>oblique</c>, or <c>normal</c>, in which case the renderer falls
        /// back to its own fixed default skew. See <see cref="FontObliqueAngleResolver"/>.
        /// </summary>
        public double? ActualObliqueSkewSinus => FontObliqueAngleResolver.ResolveSkewSinus(Style.Font.FontStyle);

        /// <summary>
        /// Computes the <see cref="RFontStyle"/> flags (italic/bold) for this box's own font-style/numeric
        /// weight - shared between <see cref="ActualFont"/> and any derived font (e.g. a synthesized
        /// small-caps run) that needs the same style bits at a different size, so the two never drift apart.
        /// </summary>
        private RFontStyle GetActualFontStyleFlags()
        {
            var st = RFontStyle.Regular;

            // FontStyle may be the bare "oblique" keyword or CSS Fonts Level 4's "oblique <angle>" form
            // (e.g. "oblique 10deg") - both are italic-equivalent for RFontStyle purposes, so match by
            // prefix rather than exact equality.
            if (Style.Font.FontStyle is Keywords.Italic || Style.Font.FontStyle.StartsWith(Keywords.Oblique, StringComparison.Ordinal))
            {
                st |= RFontStyle.Italic;
            }

            if (ActualNumericWeight >= 700)
            {
                st |= RFontStyle.Bold;
            }

            return st;
        }

        private RFont? _smallCapsFont;

        /// <summary>
        /// A cached font derived from <see cref="ActualFont"/> at a reduced size (same family/style), used
        /// to synthesize <c>font-variant: small-caps</c> - PeachPDF has no OpenType shaping engine to do
        /// real <c>smcp</c>/<c>c2sc</c> glyph substitution, so originally-lowercase runs are upper-cased and
        /// drawn at this smaller size instead. See <c>CssBox.ParseToWords</c>.
        /// </summary>
        public RFont ActualSmallCapsFont
        {
            get
            {
                if (_smallCapsFont != null) return _smallCapsFont;

                var font = ActualFont;
                _smallCapsFont = Owner.GetCachedFont(Style.Font.FontFamily!, font.Size * CssBox.SmallCapsFontScale, GetActualFontStyleFlags(), ActualNumericWeight, ActualStretch, ActualObliqueSkewSinus)
                                 ?? font;
                return _smallCapsFont;
            }
        }

        // Two fields, not one Nullable: "no synthesis needed" is itself a resolved answer worth
        // caching, and a nullable can't distinguish it from "not computed yet".
        private bool _subSuperscriptSynthesisResolved;
        private (double SizeScale, double BaselineShift)? _subSuperscriptSynthesis;

        /// <summary>
        /// The geometry for a *synthesized* sub/superscript on this box - the face scale, and the
        /// baseline shift in layout units, signed for the axis (negative = up, for a superscript).
        /// Null whenever no synthesis is needed: <c>font-variant-position: normal</c>, or a resolved font
        /// that really has the requested <c>subs</c>/<c>sups</c> GSUB feature, in which case
        /// <see cref="ActualFontVariantPosition"/> is non-None and real substitution does the work.
        /// </summary>
        /// <remarks>
        /// The numbers come from the font's own OS/2 table (<see cref="RFont.GetSubSuperscriptMetrics"/>),
        /// whose <c>ySuperscript*</c>/<c>ySubscript*</c> fields exist precisely to tell a UA how to build
        /// these - so a synthesized variant follows the type designer's intent rather than one ratio
        /// imposed on every face. The fallbacks are only for a font that leaves those fields at zero.
        /// </remarks>
        public (double SizeScale, double BaselineShift)? SubSuperscriptSynthesis
        {
            get
            {
                if (_subSuperscriptSynthesisResolved) return _subSuperscriptSynthesis;

                var requested = RequestedFontVariantPosition;
                (double SizeScale, double BaselineShift)? resolved = null;

                // Never synthesize what real GSUB substitution is already doing - requesting both would
                // shrink and shift glyphs that are already drawn as proper sub/superscripts.
                if (requested != FontVariantPositionFeature.None && ActualFontVariantPosition == FontVariantPositionFeature.None)
                {
                    var isSuper = requested == FontVariantPositionFeature.Super;
                    var font = ActualFont;

                    // Representative fallbacks for a font that states nothing: the ratios browsers use
                    // when OS/2 is unhelpful.
                    var (sizeScale, shiftEm) = font.GetSubSuperscriptMetrics(isSuper)
                                               ?? (0.583, isSuper ? 0.34 : 0.2);

                    resolved = (sizeScale, (isSuper ? -1 : 1) * shiftEm * font.Size);
                }

                _subSuperscriptSynthesis = resolved;
                _subSuperscriptSynthesisResolved = true;
                return resolved;
            }
        }

        private RFont? _subSuperscriptFont;

        /// <summary>
        /// A cached font derived from <see cref="ActualFont"/> at <see cref="SubSuperscriptSynthesis"/>'s
        /// reduced size (same family/style), used to draw a synthesized sub/superscript. Falls back to
        /// <see cref="ActualFont"/> itself when no synthesis applies or the smaller face can't be
        /// resolved.
        /// </summary>
        public RFont ActualSubSuperscriptFont
        {
            get
            {
                if (_subSuperscriptFont != null) return _subSuperscriptFont;

                var font = ActualFont;

                if (SubSuperscriptSynthesis is not { } synthesis)
                {
                    _subSuperscriptFont = font;
                    return font;
                }

                _subSuperscriptFont = Owner.GetCachedFont(Style.Font.FontFamily!, font.Size * synthesis.SizeScale, GetActualFontStyleFlags(), ActualNumericWeight, ActualStretch, ActualObliqueSkewSinus)
                                      ?? font;
                return _subSuperscriptFont;
            }
        }

        private Dictionary<(int Codepoint, double Scale), RFont>? _codepointFontCache;

        /// <summary>
        /// The font this box uses for <paramref name="codepoint"/> specifically - the first family in the
        /// <c>font-family</c> stack whose face both covers the codepoint (its <c>unicode-range</c>/cmap
        /// coverage) and has a glyph for it. Falls back to <see cref="ActualFont"/> (or
        /// <see cref="ActualSmallCapsFont"/> when <paramref name="sizeScale"/> marks a small-caps run) when
        /// no declared family covers it. Cached per (codepoint, scale); mirrors <see cref="ActualSmallCapsFont"/>'s
        /// size/style derivation.
        /// </summary>
        public RFont ActualFontForCodepoint(Rune codepoint, double sizeScale = 1.0)
        {
            var cacheKey = (codepoint.Value, sizeScale);
            if (_codepointFontCache is not null && _codepointFontCache.TryGetValue(cacheKey, out var cached))
                return cached;

            var size = ActualFont.Size * sizeScale;
            // Resolve against the full authored font-family stack (not the cascade-collapsed single family)
            // so a codepoint the first family can't supply falls back to a later one.
            var font = Owner.GetCachedFontForCodepoint(Style.Font.FontFamilyList ?? Style.Font.FontFamily!, size, GetActualFontStyleFlags(), codepoint, ActualNumericWeight, ActualStretch, ActualObliqueSkewSinus)
                       ?? (sizeScale == 1.0 ? ActualFont : ActualSmallCapsFont);

            (_codepointFontCache ??= [])[cacheKey] = font;
            return font;
        }

        /// <summary>Gets the size of 1em, per spec: an element's own computed font-size.</summary>
        public double GetEmHeight() => ActualFont.Size;

        /// <summary>
        /// The font size of the ROOT ELEMENT (css-values-3 §5.1.2), which is the outermost box that
        /// corresponds to an element.
        ///
        /// Walking all the way to the topmost <see cref="CssBox"/> instead lands on the container's
        /// own root box, which sits above &lt;html&gt; and carries no element, so its font size is
        /// always <see cref="DefaultFontResolver.FontSize"/>. Every <c>rem</c> in the document then
        /// resolves against that default no matter what the root declares: with
        /// <c>html { font-size: 32px }</c>, <c>1em</c> and <c>1rem</c> disagree in the same document.
        /// </summary>
        public double GetRemHeight()
        {
            var box = Owner;
            CssBox? rootElement = null;

            for (var parentBox = box.ParentBox; parentBox is not null; parentBox = parentBox.ParentBox)
            {
                box = parentBox;
                if (box.HtmlTag is not null)
                {
                    rootElement = box;
                }
            }

            // `box` is now the topmost box, above <html> and carrying no element, so its font size is
            // the default. That is the right answer when there is no root ELEMENT above the caller: a
            // fragment with no element boxes, and — importantly — the root element resolving its OWN
            // font size, where a rem resolves against the initial value (css-values-3 §5.1.2) and
            // consulting the root again would recurse forever through ActualFont.
            return (rootElement ?? box).GetEmHeight();
        }

        #endregion

        #region Font-relative units (ex/ch/cap/ic/lh and their root-element counterparts)

        private double?[]? _fontRatioCache;

        /// <summary>Set while this box's own <c>line-height</c> is being resolved - see <see cref="ActualLineHeight"/>.</summary>
        private bool _resolvingLineHeight;

        /// <summary>
        /// Supplies <see cref="Length.ToPixels"/> with the font measurements <c>ex</c>/<c>ch</c>/<c>cap</c>/
        /// <c>ic</c>/<c>lh</c> are defined by, each as a fraction of the em of the font it was measured
        /// from - so the caller keeps scaling it by the same em basis it already carries. The root-element
        /// variants read the root element's font, found the way <see cref="GetRemHeight"/> finds it: with no
        /// root element above this box (the root resolving its own font-size) they take the spec's fallback,
        /// which is also what keeps that resolution from recursing into itself.
        /// </summary>
        double IFontMetricSource.GetRatio(FontMetric metric, bool rootElement)
        {
            var style = rootElement ? FindRootElementStyle() : this;
            return style is null ? FontMetricRatios.Approximate(metric) : style.OwnFontRatio(metric);
        }

        /// <summary>
        /// The font basis of a <c>font-size</c> declaration: <c>ex</c>/<c>ch</c>/... resolve against the
        /// parent's font (this box's own is what is being computed), the root-element variants against the
        /// root's, found from this box.
        /// </summary>
        private sealed class ParentScopedFontMetrics(DerivedStyle? parent, DerivedStyle owner) : IFontMetricSource
        {
            public double GetRatio(FontMetric metric, bool rootElement) =>
                rootElement
                    ? ((IFontMetricSource)owner).GetRatio(metric, true)
                    : parent is null ? FontMetricRatios.Approximate(metric) : ((IFontMetricSource)parent).GetRatio(metric, false);
        }

        private DerivedStyle? FindRootElementStyle()
        {
            CssBox? rootElement = null;

            for (var parentBox = Owner.ParentBox; parentBox is not null; parentBox = parentBox.ParentBox)
            {
                if (parentBox.HtmlTag is not null)
                    rootElement = parentBox;
            }

            return rootElement?.DerivedStyle;
        }

        private double OwnFontRatio(FontMetric metric)
        {
            var pixelsPerPoint = (Owner.HtmlContainer?.Adapter as PdfSharpAdapter)?.PixelsPerPoint ?? 1.0;

            if (metric == FontMetric.Lh)
                return LineHeightRatio(pixelsPerPoint);

            // Measured once per box, like ActualFont itself: the font it reads never changes after the first read.
            var cache = _fontRatioCache ??= new double?[4];
            if (cache[(int)metric] is { } cached) return cached;

            // ic is the advance of the ideograph in the font that actually renders it - the primary face
            // only when that one covers it - and 1em when no font does (Ratio's fallback for a missing glyph).
            var font = metric == FontMetric.Ic ? ActualFontForCodepoint(FontMetricMeasurement.WaterIdeograph) : ActualFont;
            return (cache[(int)metric] = FontMetricMeasurement.Ratio(font, metric)).Value;
        }

        /// <summary>
        /// This box's used line-height as a multiple of its em, defined so that <c>1lh</c> lands exactly on
        /// <see cref="ActualLineHeight"/> once <see cref="Length.ToPixels"/> multiplies by the true-point em and
        /// the caller's <c>PixelsPerPoint</c> catch-up reapplies the second factor.
        /// </summary>
        private double LineHeightRatio(double pixelsPerPoint)
        {
            var font = ActualFont;
            var em = font.Size * pixelsPerPoint * pixelsPerPoint;
            if (em <= 0) return FontMetricRatios.Approximate(FontMetric.Lh);

            // CSS Values 4 §6.1.1: in the line-height property itself, lh is the PARENT's line-height (a box's
            // own line-height is what is being computed). The top box has no parent and the initial value is normal.
            var lineHeight = _resolvingLineHeight
                ? Owner.ParentBox?.DerivedStyle.ActualLineHeight ?? font.NormalLineHeight
                : ActualLineHeight;

            return lineHeight > 0 ? lineHeight / em : FontMetricRatios.Approximate(FontMetric.Lh);
        }

        #endregion

        #region Line-height

        /// <summary>Gets the line height. Recomputed fresh every call, not cached.</summary>
        public double ActualLineHeight
        {
            get
            {
                if (Style.Text.LineHeight.Value.Value is not { } lineHeight)
                    // `normal` (CSS 2.1 §10.8.1) resolves from the used font's own metrics, matching browsers -
                    // see RFont.NormalLineHeight (issue #956). Already fully scaled, same convention as
                    // ActualFont.Ascent/Height elsewhere - no further PixelsPerPoint correction here.
                    return ActualFont.NormalLineHeight;

                // Only a line-height that can itself contain lh (a calc might) needs the guard: there lh means
                // the parent's, and reading this box's own would recurse. rlh reads the root's, which is never
                // this box's own unless it is the root - and then there is no root above to consult. The
                // unitless/absolute majority skips the guard entirely.
                if (lineHeight.LengthOrCalc is not { } lengthOrCalc
                    || (!lengthOrCalc.IsCalc && lengthOrCalc.Length!.Value.Type != Length.Unit.Lh))
                    return CssValueParser.ParseLength(lineHeight, Owner.Size.Height, Owner);

                var wasResolving = _resolvingLineHeight;
                _resolvingLineHeight = true;
                try
                {
                    return CssValueParser.ParseLength(lineHeight, Owner.Size.Height, Owner);
                }
                finally
                {
                    _resolvingLineHeight = wasResolving;
                }
            }
        }

        #endregion

        #region Display

        /// <summary>
        /// This box's <c>display</c>, blockified per CSS 2.1 §9.7 when <see cref="Owner"/> is floated
        /// (<c>Style.DisplayPositioning.Float</c> is not <see cref="Keywords.None"/>) - the value layout
        /// and paint should actually use. <c>Style.DisplayPositioning.Display</c> itself stays the raw
        /// cascaded keyword (what the cascade produced, e.g. for CSS-OM <c>getPropertyValue</c> readback),
        /// not blockified. Recomputed fresh every call, not cached - a single enum switch, same cost class
        /// as <see cref="ActualLineHeight"/>/<see cref="IsPositioned"/> below.
        /// </summary>
        public string ActualDisplay
        {
            get
            {
                var area = Style.DisplayPositioning;
                // Floating.Footnote is excluded from blockification too: css-gcpm-3's float:footnote
                // pulls a box out of flow entirely (see DomParser.DetachFootnoteBodies), rather than
                // floating it beside its siblings the way left/right do, and the common case is an
                // inline source (a <sup> reference) that must still read as inline everywhere upstream
                // of detachment - CorrectAnonymousTables and friends, which run before detachment, would
                // otherwise see it as an ordinary block-level float and correct the tree around that
                // false premise.
                if (area.Float.Value is Floating.None or Floating.Footnote) return area.Display.ToString();

                return area.Display.Value switch
                {
                    DisplayMode.Inline => Keywords.Block,
                    DisplayMode.InlineBlock => Keywords.Block,
                    DisplayMode.InlineTable => Keywords.Table,
                    DisplayMode.TableRow => Keywords.Block,
                    DisplayMode.TableRowGroup => Keywords.Block,
                    DisplayMode.TableColumn => Keywords.Block,
                    DisplayMode.TableColumnGroup => Keywords.Block,
                    DisplayMode.TableCell => Keywords.Block,
                    DisplayMode.TableCaption => Keywords.Block,
                    DisplayMode.TableHeaderGroup => Keywords.Block,
                    DisplayMode.TableFooterGroup => Keywords.Block,
                    DisplayMode.InlineFlex => Keywords.Flex,
                    DisplayMode.InlineGrid => Keywords.Grid,
                    _ => area.Display.ToString()
                };
            }
        }

        #endregion

        #region Position, multi-column

        /// <summary>True for a positioned element: <c>position</c> of relative, absolute, fixed, sticky,
        /// or (css-gcpm-3) running - a running box is laid out standalone against a page margin box
        /// (see <c>RunningElementLayout</c>), where it must be a positioning root for its own
        /// <c>position: absolute</c> descendants the same way any other positioned box is.</summary>
        public bool IsPositioned => Style.DisplayPositioning.Position.Value is PositionMode.Relative or PositionMode.Absolute or PositionMode.Fixed or PositionMode.Sticky or PositionMode.Running;

        /// <summary>
        /// Whether this box establishes a CSS multi-column formatting context, per spec: <c>column-width</c>
        /// is not <c>auto</c>, or <c>column-count</c> is not <c>auto</c>.
        /// </summary>
        public bool EstablishesMultiColumnContext =>
            Style.MultiColumn.ColumnCount.Value is { IsValue: true } ||
            Style.MultiColumn.ColumnWidth.Value is { IsValue: true };

        #endregion
    }

    /// <summary>
    /// Holds the eight computed (overlap-reduced) corner radii for a box rectangle.
    /// </summary>
    internal readonly struct BorderRadii
    {
        public readonly double TLX, TLY, TRX, TRY, BRX, BRY, BLX, BLY;

        public BorderRadii(double tlX, double tlY, double trX, double trY,
                           double brX, double brY, double blX, double blY)
        {
            TLX = tlX; TLY = tlY;
            TRX = trX; TRY = trY;
            BRX = brX; BRY = brY;
            BLX = blX; BLY = blY;
        }

        public bool IsRounded => TLX > 0 || TLY > 0 || TRX > 0 || TRY > 0 ||
                                 BRX > 0 || BRY > 0 || BLX > 0 || BLY > 0;
    }
}
