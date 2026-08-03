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
                if (string.IsNullOrEmpty(Style.Border.BorderTopStyle) || Style.Border.BorderTopStyle == CssConstants.None)
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
                if (string.IsNullOrEmpty(Style.Border.BorderRightStyle) || Style.Border.BorderRightStyle == CssConstants.None)
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
                if (string.IsNullOrEmpty(Style.Border.BorderBottomStyle) || Style.Border.BorderBottomStyle == CssConstants.None)
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
                if (string.IsNullOrEmpty(Style.Border.BorderLeftStyle) || Style.Border.BorderLeftStyle == CssConstants.None)
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
                if (string.IsNullOrEmpty(Style.MultiColumn.ColumnRuleStyle) || Style.MultiColumn.ColumnRuleStyle == CssConstants.None)
                    _actualColumnRuleWidth = 0f;

                return _actualColumnRuleWidth;
            }
        }

        internal void InvalidateBorderTopWidth() => _actualBorderTopWidth = double.NaN;
        internal void InvalidateBorderRightWidth() => _actualBorderRightWidth = double.NaN;
        internal void InvalidateBorderBottomWidth() => _actualBorderBottomWidth = double.NaN;
        internal void InvalidateBorderLeftWidth() => _actualBorderLeftWidth = double.NaN;

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
        /// Computes overlap-reduced radii for the given rendering rectangle, per CSS spec §4.
        /// Horizontal and vertical axes are reduced independently.
        /// </summary>
        internal BorderRadii ComputeRadii(RRect rect)
        {
            double tlX = ActualBorderTopLeftRadiusX, tlY = ActualBorderTopLeftRadiusY;
            double trX = ActualBorderTopRightRadiusX, trY = ActualBorderTopRightRadiusY;
            double brX = ActualBorderBottomRightRadiusX, brY = ActualBorderBottomRightRadiusY;
            double blX = ActualBorderBottomLeftRadiusX, blY = ActualBorderBottomLeftRadiusY;

            // Horizontal reduction: check top side and bottom side independently.
            double fTop = tlX + trX > 0 && rect.Width > 0 ? rect.Width / (tlX + trX) : 1.0;
            double fBot = blX + brX > 0 && rect.Width > 0 ? rect.Width / (blX + brX) : 1.0;
            double fX = Math.Min(1.0, Math.Min(fTop, fBot));

            // Vertical reduction: check left side and right side independently.
            double fLeft = tlY + blY > 0 && rect.Height > 0 ? rect.Height / (tlY + blY) : 1.0;
            double fRight = trY + brY > 0 && rect.Height > 0 ? rect.Height / (trY + brY) : 1.0;
            double fY = Math.Min(1.0, Math.Min(fLeft, fRight));

            return new BorderRadii(tlX * fX, tlY * fY, trX * fX, trY * fY,
                brX * fX, brY * fY, blX * fX, blY * fY);
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

            _actualLetterSpacing = Style.Text.LetterSpacing == CssConstants.Normal
                ? 0
                : CssValueParser.ParseLength(Style.Text.LetterSpacing, 1, Owner);
        }

        public double ActualTextIndent
        {
            get
            {
                if (double.IsNaN(_actualTextIndent))
                    _actualTextIndent = CssValueParser.ParseLength(Style.Text.TextIndent, Owner.Size.Width, Owner);
                return _actualTextIndent;
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
                if (value == CssConstants.None)
                {
                    _actualFontVariantLigatures = LigatureFeatures.Required;
                    return LigatureFeatures.Required;
                }

                var tokens = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var resolved = tokens.Contains(CssConstants.NoCommonLigatures) ? LigatureFeatures.Required : LigatureFeatures.Default;
                if (tokens.Contains(CssConstants.DiscretionaryLigatures)) resolved |= LigatureFeatures.Discretionary;
                if (tokens.Contains(CssConstants.HistoricalLigatures)) resolved |= LigatureFeatures.Historical;

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
                    CssConstants.SmallCaps => FontVariantCapsFeature.SmallCaps,
                    CssConstants.AllSmallCaps => FontVariantCapsFeature.AllSmallCaps,
                    CssConstants.PetiteCaps => FontVariantCapsFeature.PetiteCaps,
                    CssConstants.AllPetiteCaps => FontVariantCapsFeature.AllPetiteCaps,
                    CssConstants.Unicase => FontVariantCapsFeature.Unicase,
                    CssConstants.TitlingCaps => FontVariantCapsFeature.TitlingCaps,
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
                        CssConstants.LiningNums => NumericFeatures.LiningNums,
                        CssConstants.OldstyleNums => NumericFeatures.OldstyleNums,
                        CssConstants.ProportionalNums => NumericFeatures.ProportionalNums,
                        CssConstants.TabularNums => NumericFeatures.TabularNums,
                        CssConstants.DiagonalFractions => NumericFeatures.DiagonalFractions,
                        CssConstants.StackedFractions => NumericFeatures.StackedFractions,
                        CssConstants.Ordinal => NumericFeatures.Ordinal,
                        CssConstants.SlashedZero => NumericFeatures.SlashedZero,
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
                        CssConstants.Jis78Forms => EastAsianFeatures.Jis78,
                        CssConstants.Jis83Forms => EastAsianFeatures.Jis83,
                        CssConstants.Jis90Forms => EastAsianFeatures.Jis90,
                        CssConstants.Jis04Forms => EastAsianFeatures.Jis04,
                        CssConstants.Simplified => EastAsianFeatures.Simplified,
                        CssConstants.Traditional => EastAsianFeatures.Traditional,
                        CssConstants.FullWidth => EastAsianFeatures.FullWidth,
                        CssConstants.ProportionalWidth => EastAsianFeatures.ProportionalWidth,
                        CssConstants.Ruby => EastAsianFeatures.Ruby,
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
                var resolved = value == CssConstants.Normal
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
                    if (rawValue == CssConstants.Off) settingValue = 0;
                    else if (rawValue != CssConstants.On) int.TryParse(rawValue, out settingValue);
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
                    Owner.FontFamily = CssConstants.DefaultFont;
                }

                if (string.IsNullOrEmpty(Style.Font.FontSize))
                {
                    Owner.FontSize = CssConstants.FontSize.ToString(CultureInfo.InvariantCulture) + "pt";
                }

                var st = GetActualFontStyleFlags();

                double parentSize = CssConstants.FontSize;
                double remSize;

                var parentBox = Owner.ParentBox;

                if (parentBox is not null)
                {
                    parentSize = parentBox.ActualFont.Size;
                    remSize = GetRemHeight();
                }
                else
                {
                    remSize = CssConstants.FontSize;
                }

                // For a box with text content this basis is unreachable in practice - word-splitting
                // (DomParser.CorrectTextBoxes, during cascade) reads ActualFont before any layout pass
                // exists, caching this size permanently against a container that hasn't been laid out yet.
                // Correct only for a box whose ActualFont happens to first be read post-layout - see
                // .claude/accepted-gaps/font-size-container-relative-units-resolve-to-zero-for-text-content.md.
                var (containerInlinePt, containerBlockPt) = Owner.GetContainerRelativeUnitBasis();
                var fsize = FontSizeResolver.Resolve(Style.Font.FontSize, parentSize, remSize,
                    containerInlinePt, containerBlockPt);

                _actualFont = Owner.GetCachedFont(Style.Font.FontFamily!, fsize, st, ActualNumericWeight, ActualStretch, ActualObliqueSkewSinus)
                              ?? Owner.GetCachedFont(CssConstants.DefaultFont, fsize, st, ActualNumericWeight, ActualStretch, ActualObliqueSkewSinus);

                if (_actualFont is null)
                {
                    throw new HtmlRenderException($"Cannot find font: {Style.Font.FontFamily} and Default Font {CssConstants.DefaultFont} is not installed", HtmlRenderErrorType.General);
                }

                return _actualFont!;
            }
        }

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
                var resolved = FontWeightResolver.Resolve(Style.Font.FontWeight, parentWeight);
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

                var resolved = FontStretchResolver.Resolve(Style.Font.FontStretch);
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
            if (Style.Font.FontStyle is CssConstants.Italic || Style.Font.FontStyle.StartsWith(CssConstants.Oblique, StringComparison.Ordinal))
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
        public double ActualLineHeight => Style.Text.LineHeight is CssConstants.Normal
            ? 1.2 * GetEmHeight()
            : CssValueParser.ParseLength(Style.Text.LineHeight, Owner.Size.Height, Owner);

        #endregion

        #region Position, multi-column

        /// <summary>True for a positioned element: <c>position</c> of relative, absolute, fixed, or sticky.</summary>
        public bool IsPositioned => Style.DisplayPositioning.Position is CssConstants.Relative or CssConstants.Absolute or CssConstants.Fixed or CssConstants.Sticky;

        /// <summary>
        /// Whether this box establishes a CSS multi-column formatting context, per spec: <c>column-width</c>
        /// is not <c>auto</c>, or <c>column-count</c> is not <c>auto</c>.
        /// </summary>
        public bool EstablishesMultiColumnContext =>
            (!string.IsNullOrEmpty(Style.MultiColumn.ColumnCount) && Style.MultiColumn.ColumnCount != CssConstants.Auto) ||
            (!string.IsNullOrEmpty(Style.MultiColumn.ColumnWidth) && Style.MultiColumn.ColumnWidth != CssConstants.Auto);

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
