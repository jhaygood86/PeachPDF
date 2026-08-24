using PeachPDF.Adapters;
using PeachPDF.CSS;
using PeachPDF.Html.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core.Parse;
using PeachPDF.Html.Core.Utils;
using PeachPDF.Text;
using System;
using System.Collections.Generic;
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
    internal sealed record DerivedStyle(CssBox Owner)
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
                if (Style.Border.BorderTopStyle.Value == LineStyle.None)
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
                if (Style.Border.BorderRightStyle.Value == LineStyle.None)
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
                if (Style.Border.BorderBottomStyle.Value == LineStyle.None)
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
                if (Style.Border.BorderLeftStyle.Value == LineStyle.None)
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
                if (Style.MultiColumn.ColumnRuleStyle.Value == LineStyle.None)
                    _actualColumnRuleWidth = 0f;

                return _actualColumnRuleWidth;
            }
        }

        /// <summary>
        /// The declared border width CSS 2.1 §17.6.2 resolution itself needs as an input - deliberately
        /// bypassing <see cref="_actualBorderTopWidth"/>'s cache, which <see cref="SetCollapsedUsedBorderWidths"/>
        /// overwrites with the box-model *used* half-width for the rest of a collapsed table's layout
        /// pass. <see cref="CollapsedBorderModel.Resolve"/> itself runs before that override is applied
        /// (safe to read <see cref="ActualBorderTopWidth"/> directly), but
        /// <see cref="CollapsedBorderModel.ResolveRepeatedGroupBoundary"/> runs at the very end of the
        /// pass, once a repeated group's final per-page geometry exists - by then the override is
        /// already active, and reading the cached property there would resolve against half of an
        /// earlier, unrelated resolution instead of this cell's real declared border.
        /// </summary>
        internal double NaturalBorderTopWidth =>
            Style.Border.BorderTopStyle.Value == LineStyle.None
                ? 0d : CssValueParser.GetActualBorderWidth(Style.Border.BorderTopWidth, Owner);

        internal double NaturalBorderRightWidth =>
            Style.Border.BorderRightStyle.Value == LineStyle.None
                ? 0d : CssValueParser.GetActualBorderWidth(Style.Border.BorderRightWidth, Owner);

        internal double NaturalBorderBottomWidth =>
            Style.Border.BorderBottomStyle.Value == LineStyle.None
                ? 0d : CssValueParser.GetActualBorderWidth(Style.Border.BorderBottomWidth, Owner);

        internal double NaturalBorderLeftWidth =>
            Style.Border.BorderLeftStyle.Value == LineStyle.None
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
                if (_actualBorderTopColor.IsEmpty) _actualBorderTopColor = Owner.GetActualColor(Style.Border.BorderTopColor);
                return _actualBorderTopColor;
            }
        }

        public RColor ActualBorderRightColor
        {
            get
            {
                if (_actualBorderRightColor.IsEmpty) _actualBorderRightColor = Owner.GetActualColor(Style.Border.BorderRightColor);
                return _actualBorderRightColor;
            }
        }

        public RColor ActualBorderBottomColor
        {
            get
            {
                if (_actualBorderBottomColor.IsEmpty) _actualBorderBottomColor = Owner.GetActualColor(Style.Border.BorderBottomColor);
                return _actualBorderBottomColor;
            }
        }

        public RColor ActualBorderLeftColor
        {
            get
            {
                if (_actualBorderLeftColor.IsEmpty) _actualBorderLeftColor = Owner.GetActualColor(Style.Border.BorderLeftColor);
                return _actualBorderLeftColor;
            }
        }

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
                    _actualTransformMatrix = CssValueParser.ParseTransform(Style.VisualEffects.Transform, Style.VisualEffects.TransformOrigin, Owner);
                    _actualTransformComputed = true;
                }
                return _actualTransformMatrix;
            }
        }

        /// <summary>True when this box has a non-identity CSS transform to apply at paint time.</summary>
        public bool IsTransformed => !ActualTransformMatrix.IsIdentity;

        internal void InvalidateTransform() => _actualTransformComputed = false;

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

            var tokens = CssValueParser.GetCssTokens(Style.Text.TextIndent);
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
        /// text, from the CSS <c>font-variant-ligatures</c> value. The common-ligatures axis (and
        /// <c>none</c>) and the discretionary/historical axes all change shaping (real <c>dlig</c>/
        /// <c>hlig</c> GSUB substitution, since both are Lookup Type 4 like common ligatures) -
        /// <c>contextual</c> still parses but doesn't yet affect it (no <c>calt</c> lookup application,
        /// which needs GSUB chaining-context lookup types this codebase doesn't read). Per the CSS
        /// Fonts spec, required ligatures (<c>rlig</c>) are never affected by this property - not even
        /// by <c>none</c> - so the common-ligatures-off case still resolves to
        /// <see cref="LigatureFeatures.Required"/> rather than <see cref="LigatureFeatures.None"/>.
        /// </summary>
        public LigatureFeatures ActualFontVariantLigatures
        {
            get
            {
                if (_actualFontVariantLigatures is { } cached) return cached;

                var value = Style.Font.FontVariantLigatures;
                if (value == Keywords.None)
                {
                    _actualFontVariantLigatures = LigatureFeatures.Required;
                    return LigatureFeatures.Required;
                }

                var tokens = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var resolved = tokens.Contains(Keywords.NoCommonLigatures) ? LigatureFeatures.Required : LigatureFeatures.Default;
                if (tokens.Contains(Keywords.DiscretionaryLigatures)) resolved |= LigatureFeatures.Discretionary;
                if (tokens.Contains(Keywords.HistoricalLigatures)) resolved |= LigatureFeatures.Historical;

                _actualFontVariantLigatures = resolved;
                return resolved;
            }
        }

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

                var requested = Style.Font.FontVariantCaps switch
                {
                    Keywords.SmallCaps => FontVariantCapsFeature.SmallCaps,
                    Keywords.AllSmallCaps => FontVariantCapsFeature.AllSmallCaps,
                    Keywords.PetiteCaps => FontVariantCapsFeature.PetiteCaps,
                    Keywords.AllPetiteCaps => FontVariantCapsFeature.AllPetiteCaps,
                    Keywords.Unicase => FontVariantCapsFeature.Unicase,
                    Keywords.TitlingCaps => FontVariantCapsFeature.TitlingCaps,
                    _ => FontVariantCapsFeature.None,
                };

                var resolved = requested != FontVariantCapsFeature.None && ActualFont.SupportsFontVariantCaps(requested)
                    ? requested
                    : FontVariantCapsFeature.None;

                _actualFontVariantCaps = resolved;
                return resolved;
            }
        }

        private NumericFeatures? _actualFontVariantNumeric;

        /// <summary>The resolved GSUB numeric features (CSS <c>font-variant-numeric</c>) for this box's
        /// text - no capability gating (unlike caps): a tag the resolved font lacks simply activates no
        /// lookup and is silently inert.</summary>
        public NumericFeatures ActualFontVariantNumeric
        {
            get
            {
                if (_actualFontVariantNumeric is { } cached) return cached;

                var resolved = NumericFeatures.None;
                foreach (var token in Style.Font.FontVariantNumeric.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    resolved |= token switch
                    {
                        Keywords.LiningNums => NumericFeatures.LiningNums,
                        Keywords.OldstyleNums => NumericFeatures.OldstyleNums,
                        Keywords.ProportionalNums => NumericFeatures.ProportionalNums,
                        Keywords.TabularNums => NumericFeatures.TabularNums,
                        Keywords.DiagonalFractions => NumericFeatures.DiagonalFractions,
                        Keywords.StackedFractions => NumericFeatures.StackedFractions,
                        Keywords.Ordinal => NumericFeatures.Ordinal,
                        Keywords.SlashedZero => NumericFeatures.SlashedZero,
                        _ => NumericFeatures.None,
                    };
                }

                _actualFontVariantNumeric = resolved;
                return resolved;
            }
        }

        private EastAsianFeatures? _actualFontVariantEastAsian;

        /// <summary>The resolved GSUB east-asian features (CSS <c>font-variant-east-asian</c>) for this
        /// box's text - no capability gating, same rationale as <see cref="ActualFontVariantNumeric"/>.</summary>
        public EastAsianFeatures ActualFontVariantEastAsian
        {
            get
            {
                if (_actualFontVariantEastAsian is { } cached) return cached;

                var resolved = EastAsianFeatures.None;
                foreach (var token in Style.Font.FontVariantEastAsian.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    resolved |= token switch
                    {
                        Keywords.Jis78Forms => EastAsianFeatures.Jis78,
                        Keywords.Jis83Forms => EastAsianFeatures.Jis83,
                        Keywords.Jis90Forms => EastAsianFeatures.Jis90,
                        Keywords.Jis04Forms => EastAsianFeatures.Jis04,
                        Keywords.Simplified => EastAsianFeatures.Simplified,
                        Keywords.Traditional => EastAsianFeatures.Traditional,
                        Keywords.FullWidth => EastAsianFeatures.FullWidth,
                        Keywords.ProportionalWidth => EastAsianFeatures.ProportionalWidth,
                        Keywords.Ruby => EastAsianFeatures.Ruby,
                        _ => EastAsianFeatures.None,
                    };
                }

                _actualFontVariantEastAsian = resolved;
                return resolved;
            }
        }

        private IReadOnlyList<(string Tag, int Value)>? _actualFontFeatureSettings;

        /// <summary>
        /// The resolved explicit OpenType feature tags (CSS <c>font-feature-settings</c>) for this
        /// box's text, parsed from the cascaded string (e.g. <c>"smcp" 1, "onum" 1</c>) into
        /// (tag, value) pairs - <c>on</c>/<c>off</c> resolve to 1/0, a bare tag with no value defaults
        /// to 1. <c>normal</c> resolves to an empty list.
        /// </summary>
        public IReadOnlyList<(string Tag, int Value)> ActualFontFeatureSettings
        {
            get
            {
                if (_actualFontFeatureSettings is { } cached) return cached;

                var value = Style.Font.FontFeatureSettings;
                var resolved = value == Keywords.Normal
                    ? []
                    : ParseFontFeatureSettings(value);

                _actualFontFeatureSettings = resolved;
                return resolved;
            }
        }

        private static IReadOnlyList<(string Tag, int Value)> ParseFontFeatureSettings(string value)
        {
            var entries = new List<(string, int)>();

            foreach (var rawEntry in value.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = rawEntry.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0) continue;

                var tag = parts[0].Trim('"');
                var settingValue = 1;
                if (parts.Length > 1)
                {
                    var rawValue = parts[1];
                    if (rawValue == Keywords.Off) settingValue = 0;
                    else if (rawValue != Keywords.On) int.TryParse(rawValue, out settingValue);
                }

                entries.Add((tag, settingValue));
            }

            return entries;
        }

        private TextShapingFeatures? _actualTextShapingFeatures;

        /// <summary>
        /// The single combined GSUB feature request for this box's text - ligatures, caps, numeric,
        /// east-asian, and explicit <c>font-feature-settings</c> tags all folded into one
        /// <see cref="TextShapingFeatures"/> value, the one actually threaded into every measure/paint
        /// call site.
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
                    ActualFontFeatureSettings);

                _actualTextShapingFeatures = resolved;
                return resolved;
            }
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
                var fsize = FontSizeResolver.Resolve(Style.Font.FontSize.Value, parentSize, remSize,
                    containerInlinePt, containerBlockPt, viewportWidthPt, viewportHeightPt,
                    containerWidthPt, containerHeightPt, viewportInlinePt, viewportBlockPt);

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

        /// <summary>Gets the height of the root font.</summary>
        public double GetRemHeight()
        {
            var box = Owner;
            var parentBox = box.ParentBox;

            while (parentBox is not null)
            {
                box = parentBox;
                parentBox = box.ParentBox;
            }

            return box.GetEmHeight();
        }

        #endregion

        #region Line-height

        /// <summary>Gets the line height. Recomputed fresh every call, not cached.</summary>
        public double ActualLineHeight => Style.Text.LineHeight.Value.Value is { } lineHeight
            ? CssValueParser.ParseLength(lineHeight, Owner.Size.Height, Owner)
            // GetEmHeight() is device-scaled (see CssValueParser.ParseLength(LengthOrUnitless,...)'s
            // identical correction for line-height's own explicit unitless multiplier) - undo that the
            // same way for the normal/default 1.2 multiplier (issue #814's line-height sibling).
            : 1.2 * GetEmHeight() * ((Owner.HtmlContainer?.Adapter as PdfSharpAdapter)?.PixelsPerPoint ?? 1.0);

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
