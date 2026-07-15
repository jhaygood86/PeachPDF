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

using PeachPDF;
using PeachPDF.CSS;
using PeachPDF.Html.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core.Entities;
using PeachPDF.Html.Core.Parse;
using PeachPDF.Html.Core.Utils;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace PeachPDF.Html.Core.Dom
{
    /// <summary>
    /// Base class for css box to handle the css properties.<br/>
    /// Has field and property for every css property that can be set, the properties add additional parsing like
    /// setting the correct border depending what border value was set (single, two , all four).<br/>
    /// Has additional fields to control the location and size of the box and 'actual' css values for some properties
    /// that require additional calculations and parsing.<br/>
    /// </summary>
    internal abstract class CssBoxProperties
    {
        #region CSS Fields

        private string _borderTopWidth = "medium";
        private string _borderRightWidth = "medium";
        private string _borderBottomWidth = "medium";
        private string _borderLeftWidth = "medium";
        private string _borderTopColor = "black";
        private string _borderRightColor = "black";
        private string _borderBottomColor = "black";
        private string _borderLeftColor = "black";
        private string? _bottom;
        private string _color = "black";
        private string _fontSize = "medium";
        private string _paddingLeft = "0";
        private string _paddingBottom = "0";
        private string _paddingRight = "0";
        private string _paddingTop = "0";
        private string? _right;
        private string _textIndent = "0";
        private string _wordSpacing = "normal";

        /// <summary>
        /// Specified (not var()-resolved) values of this box's CSS custom properties (--foo), keyed by
        /// their case-sensitive name. Lazily created; null when no custom property has been declared or inherited.
        /// </summary>
        internal Dictionary<string, string>? CustomProperties;

        #endregion


        #region Fields

        private double _actualBorderTopLeftRadiusX = double.NaN;
        private double _actualBorderTopLeftRadiusY = double.NaN;
        private double _actualBorderTopRightRadiusX = double.NaN;
        private double _actualBorderTopRightRadiusY = double.NaN;
        private double _actualBorderBottomRightRadiusX = double.NaN;
        private double _actualBorderBottomRightRadiusY = double.NaN;
        private double _actualBorderBottomLeftRadiusX = double.NaN;
        private double _actualBorderBottomLeftRadiusY = double.NaN;
        private RColor _actualColor = RColor.Empty;
        private double _actualPaddingTop = double.NaN;
        private double _actualPaddingBottom = double.NaN;
        private double _actualPaddingRight = double.NaN;
        private double _actualPaddingLeft = double.NaN;
        private double _collapsedMarginTop = double.NaN;
        private double _actualBorderTopWidth = double.NaN;
        private double _actualBorderLeftWidth = double.NaN;
        private double _actualBorderBottomWidth = double.NaN;
        private double _actualBorderRightWidth = double.NaN;
        private double _actualColumnRuleWidth = double.NaN;

        /// <summary>
        /// the width of whitespace between words
        /// </summary>
        private double _actualWordSpacing = double.NaN;
        private double _actualTextIndent = double.NaN;
        private double _actualBorderSpacingHorizontal = double.NaN;
        private double _actualBorderSpacingVertical = double.NaN;
        private RColor _actualBorderTopColor = RColor.Empty;
        private RColor _actualBorderLeftColor = RColor.Empty;
        private RColor _actualBorderBottomColor = RColor.Empty;
        private RColor _actualBorderRightColor = RColor.Empty;
        private RColor _actualColumnRuleColor = RColor.Empty;
        private RColor _actualBackgroundColor = RColor.Empty;
        private RFont? _actualFont;
        private string _display = "inline";

        #endregion


        #region CSS Properties

        public string BorderBottomWidth
        {
            get => _borderBottomWidth;
            set
            {
                _borderBottomWidth = value;
                _actualBorderBottomWidth = float.NaN;
            }
        }

        public string BorderLeftWidth
        {
            get => _borderLeftWidth;
            set
            {
                _borderLeftWidth = value;
                _actualBorderLeftWidth = float.NaN;
            }
        }

        public string BorderRightWidth
        {
            get => _borderRightWidth;
            set
            {
                _borderRightWidth = value;
                _actualBorderRightWidth = float.NaN;
            }
        }

        public string BorderTopWidth
        {
            get => _borderTopWidth;
            set
            {
                _borderTopWidth = value;
                _actualBorderTopWidth = float.NaN;
            }
        }

        public string BorderBottomStyle { get; set; } = "none";

        public string BorderLeftStyle { get; set; } = "none";

        public string BorderRightStyle { get; set; } = "none";

        public string BorderTopStyle { get; set; } = "none";

        public string BorderBottomColor
        {
            get => _borderBottomColor;
            set
            {
                _borderBottomColor = value;
                _actualBorderBottomColor = RColor.Empty;
            }
        }

        public string BorderLeftColor
        {
            get => _borderLeftColor;
            set
            {
                _borderLeftColor = value;
                _actualBorderLeftColor = RColor.Empty;
            }
        }

        public string BorderRightColor
        {
            get => _borderRightColor;
            set
            {
                _borderRightColor = value;
                _actualBorderRightColor = RColor.Empty;
            }
        }

        public string BorderTopColor
        {
            get => _borderTopColor;
            set
            {
                _borderTopColor = value;
                _actualBorderTopColor = RColor.Empty;
            }
        }

        public string BorderSpacing { get; set; } = "0";

        public string BorderCollapse { get; set; } = "separate";
        public string BoxSizing { get; set; } = CssConstants.ContentBox;

        public string BorderRadius
        {
            set
            {
                var slash = value.IndexOf('/');
                var hGroup = (slash >= 0 ? value[..slash] : value).Trim();
                var vGroup = slash >= 0 ? value[(slash + 1)..].Trim() : hGroup;

                var h = ExpandRadiusShorthand(hGroup);
                var v = ExpandRadiusShorthand(vGroup);

                // Store each corner as "hValue vValue" so the computed properties
                // can extract both axes independently.
                BorderTopLeftRadius = $"{h[0]} {v[0]}";
                BorderTopRightRadius = $"{h[1]} {v[1]}";
                BorderBottomRightRadius = $"{h[2]} {v[2]}";
                BorderBottomLeftRadius = $"{h[3]} {v[3]}";
            }
        }

        // Expands a 1–4 value group into [TL, TR, BR, BL] per CSS shorthand rules.
        private static string[] ExpandRadiusShorthand(string group)
        {
            var tokens = group.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            return tokens.Length switch
            {
                1 => [tokens[0], tokens[0], tokens[0], tokens[0]],
                2 => [tokens[0], tokens[1], tokens[0], tokens[1]],
                3 => [tokens[0], tokens[1], tokens[2], tokens[1]],
                4 => [tokens[0], tokens[1], tokens[2], tokens[3]],
                _ => ["0", "0", "0", "0"]
            };
        }

        private string _transform = CssConstants.None;
        public string Transform
        {
            get => _transform;
            set
            {
                _transform = value;
                _actualTransformComputed = false;
            }
        }

        private string _transformOrigin = "50% 50% 0";
        public string TransformOrigin
        {
            get => _transformOrigin;
            set
            {
                _transformOrigin = value;
                _actualTransformComputed = false;
            }
        }

        private string _opacity = "1";
        public string Opacity
        {
            get => _opacity;
            set
            {
                _opacity = value;
                _actualOpacityComputed = false;
            }
        }

        private string _borderTopLeftRadius = "0";
        private string _borderTopRightRadius = "0";
        private string _borderBottomRightRadius = "0";
        private string _borderBottomLeftRadius = "0";

        public string BorderTopLeftRadius
        {
            get => _borderTopLeftRadius;
            set
            {
                _borderTopLeftRadius = value;
                _actualBorderTopLeftRadiusX = double.NaN;
                _actualBorderTopLeftRadiusY = double.NaN;
            }
        }

        public string BorderTopRightRadius
        {
            get => _borderTopRightRadius;
            set
            {
                _borderTopRightRadius = value;
                _actualBorderTopRightRadiusX = double.NaN;
                _actualBorderTopRightRadiusY = double.NaN;
            }
        }

        public string BorderBottomRightRadius
        {
            get => _borderBottomRightRadius;
            set
            {
                _borderBottomRightRadius = value;
                _actualBorderBottomRightRadiusX = double.NaN;
                _actualBorderBottomRightRadiusY = double.NaN;
            }
        }

        public string BorderBottomLeftRadius
        {
            get => _borderBottomLeftRadius;
            set
            {
                _borderBottomLeftRadius = value;
                _actualBorderBottomLeftRadiusX = double.NaN;
                _actualBorderBottomLeftRadiusY = double.NaN;
            }
        }
        public string CounterIncrement { get; set; } = CssConstants.None;
        public string CounterReset { get; set; } = CssConstants.None;
        public string CounterSet { get; set; } = CssConstants.None;
        public string StringSet { get; set; } = CssConstants.None;
        public string PageName { get; set; } = string.Empty;

        public string MarginBottom { get; set; } = "0";

        public string MarginLeft { get; set; } = "0";

        public string MarginRight { get; set; } = "0";

        public string MarginTop { get; set; } = "0";

        public string PaddingBottom
        {
            get => _paddingBottom;
            set
            {
                _paddingBottom = value;
                _actualPaddingBottom = double.NaN;
            }
        }

        public string PaddingLeft
        {
            get => _paddingLeft;
            set
            {
                _paddingLeft = value;
                _actualPaddingLeft = double.NaN;
            }
        }

        public string PaddingRight
        {
            get => _paddingRight;
            set
            {
                _paddingRight = value;
                _actualPaddingRight = double.NaN;
            }
        }

        public string PaddingTop
        {
            get => _paddingTop;
            set
            {
                _paddingTop = value;
                _actualPaddingTop = double.NaN;
            }
        }

        public string BreakBefore { get; set; } = CssConstants.Auto;
        public string BreakInside { get; set; } = CssConstants.Auto;
        public string BreakAfter { get; set; } = CssConstants.Auto;

        // Parsed, cascaded, and inherited like any other CSS property, but currently have no effect on
        // pagination: PeachPDF's block flow relies on paint-time per-page clipping (no explicit per-line
        // page-break decision to arbitrate) and the multicol engine only fragments at whole-child
        // granularity (see CssLayoutEngineColumns), so neither has a break point orphans/widows could
        // apply to. Same posture as background-attachment elsewhere in this file.
        public string Orphans { get; set; } = "2";
        public string Widows { get; set; } = "2";

        public string Left { get; set; } = CssConstants.Auto;

        public string Top { get; set; } = CssConstants.Auto;

        public string Bottom { get; set; } = CssConstants.Auto;

        public string Right { get; set; } = CssConstants.Auto;

        public string Width { get; set; } = "auto";

        public string MaxWidth { get; set; } = "none";
        public string MinWidth { get; set; } = "0";

        public string Height { get; set; } = "auto";
        public string MaxHeight { get; set; } = "none";
        public string MinHeight { get; set; } = "auto";

        public string BackgroundColor { get; set; } = "transparent";

        public IReadOnlyList<CssImage>? BackgroundImages { get; set; }

        public string BackgroundPosition { get; set; } = "0% 0%";

        public string BackgroundRepeat { get; set; } = "repeat";

        public string BackgroundSize { get; set; } = CssConstants.Auto;

        public string BackgroundOrigin { get; set; } = CssConstants.PaddingBox;

        public string BackgroundClip { get; set; } = CssConstants.BorderBox;

        public string Color
        {
            get => _color;
            set
            {
                _color = value;
                _actualColor = RColor.Empty;
            }
        }

        public string Content { get; set; } = CssConstants.Normal;
        public string Display
        {
            get
            {

                if (Float is not CssConstants.None)
                {
                    return _display switch
                    {
                        "inline" => "block",
                        "inline-block" => "block",
                        "inline-table" => "table",
                        "table-row" => "block",
                        "table-row-group" => "block",
                        "table-column" => "block",
                        "table-column-group" => "block",
                        "table-cell" => "block",
                        "table-caption" => "block",
                        "table-header-group" => "block",
                        "table-footer-group" => "block",
                        "inline-flex" => "flex",
                        "inline-grid" => "grid",
                        _ => _display
                    };
                }

                return _display;
            }
            set => _display = value;
        }

        public string Direction { get; set; } = "ltr";

        public string EmptyCells { get; set; } = "show";

        public string Float { get; set; } = CssConstants.None;

        public string Clear { get; set; } = CssConstants.None;

        public string Position { get; set; } = "static";

        public string LineHeight { get; set; } = "normal";

        public string VerticalAlign { get; set; } = "baseline";

        public string TextIndent
        {
            get => _textIndent;
            set => _textIndent = NoEms(value);
        }

        public string TextAlign { get; set; } = string.Empty;

        public string TextDecoration { get; set; } = string.Empty;

        public string TextDecorationLine { get; set; } = string.Empty;

        public string TextDecorationStyle { get; set; } = string.Empty;

        public string TextDecorationColor { get; set; } = string.Empty;

        public string TextTransform { get; set; } = CssConstants.None;

        public string WhiteSpace { get; set; } = "normal";

        public string Visibility { get; set; } = "visible";

        public string WordSpacing
        {
            get => _wordSpacing;
            set => _wordSpacing = NoEms(value);
        }

        public string WordBreak { get; set; } = "normal";

        /// <summary>
        /// <c>none</c>/<c>manual</c>/<c>auto</c>. <c>manual</c> and <c>auto</c> both honor an explicit
        /// soft hyphen (U+00AD) as a line-break opportunity; only <c>auto</c> additionally implies
        /// dictionary-based automatic hyphenation, which PeachPDF does not implement (see
        /// docs/html-css-support.md) — so in practice <c>auto</c> behaves like <c>manual</c> here.
        /// </summary>
        public string Hyphens { get; set; } = "manual";

        public string? FontFamily { get; set; }

        public string FontSize
        {
            get => _fontSize;
            set
            {
                // calc()-family text is resolved directly by ActualFont's own ParseLength call later
                // (including any em/rem terms it contains) - leave it untouched here. A plain "Nem" is
                // the only case needing eager conversion at this point (to points, using the parent's
                // already-resolved font size); everything else - keywords like "medium"/"larger", or any
                // other single-unit length - is left as-is for ActualFont's own resolution later.
                if (!CssValueParser.IsCalcFunction(value) &&
                    CssValueParser.GetCssTokens(value) is [UnitToken unitToken] &&
                    Length.GetUnit(unitToken.Unit) == Length.Unit.Em &&
                    GetParent() is { } parent)
                {
                    var points = unitToken.Value * parent.ActualFont.Size;
                    _fontSize = $"{points.ToString("0.0", NumberFormatInfo.InvariantInfo)}pt";
                }
                else
                {
                    _fontSize = value;
                }
            }
        }

        public string FontStyle { get; set; } = "normal";

        public string FontVariant { get; set; } = "normal";

        public string FontWeight { get; set; } = "normal";

        public string Overflow { get; set; } = "visible";

        public string ListStylePosition { get; set; } = "outside";

        public CssImage? ListStyleImage { get; set; }

        public string ListStyleType { get; set; } = "disc";

        #endregion CSS Propertier

        /// <summary>
        /// Gets or sets the location of the box
        /// </summary>
        public RPoint Location { get; set; }

        /// <summary>
        /// Gets or sets the size of the box
        /// </summary>
        public RSize Size { get; set; }

        /// <summary>
        /// Gets the bounds of the box
        /// </summary>
        public RRect Bounds
        {
            get
            {
                var boundingBoxSize = new RSize(ActualBoxSizingWidth, ActualBoxSizingHeight);

                return new RRect(Location, boundingBoxSize);
            }
        }

        /// <summary>
        /// Gets the width available on the box, counting padding and margin.
        /// </summary>
        public double AvailableWidth => ActualBoxSizingWidth - ActualBorderLeftWidth - ActualPaddingLeft - ActualPaddingRight - ActualBorderRightWidth;

        public double ActualBoxSizeIncludedWidth
        {
            get
            {
                return BoxSizing switch
                {
                    CssConstants.ContentBox => ActualPaddingLeft + ActualPaddingRight + ActualBorderLeftWidth + ActualBorderRightWidth,
                    CssConstants.BorderBox => 0,
                    _ => throw new HtmlRenderException("Unknown box sizing", HtmlRenderErrorType.Layout)
                };
            }
        }

        public double ActualBoxSizingWidth => Size.Width + ActualBoxSizeIncludedWidth;

        public double ActualBoxSizeIncludedHeight
        {
            get
            {
                return BoxSizing switch
                {
                    CssConstants.ContentBox => ActualPaddingTop + ActualPaddingBottom + ActualBorderTopWidth + ActualBorderBottomWidth,
                    CssConstants.BorderBox => 0,
                    _ => throw new HtmlRenderException("Unknown box sizing", HtmlRenderErrorType.Layout)
                };
            }
        }

        public double ActualBoxSizingHeight => Size.Height + ActualBoxSizeIncludedHeight;

        /// <summary>
        /// Gets the right of the box. When setting, it will affect only the width of the box.
        /// </summary>
        public double ActualRight
        {
            get => Location.X + ActualBoxSizingWidth;
            set => Size = new RSize(value - ActualBoxSizeIncludedWidth - Location.X, Size.Height);
        }

        /// <summary>
        /// Gets or sets the bottom of the box. 
        /// (When setting, alters only the Size.Height of the box)
        /// </summary>
        public double ActualBottom
        {
            get => Location.Y + ActualBoxSizingHeight;
            set => Size = new RSize(Size.Width, value - ActualBoxSizeIncludedHeight - Location.Y);
        }

        /// <summary>
        /// Gets the left of the client rectangle (Where content starts rendering)
        /// </summary>
        public double ClientLeft => Location.X + ActualBorderLeftWidth + ActualPaddingLeft;

        /// <summary>
        /// Gets the top of the client rectangle (Where content starts rendering)
        /// </summary>
        public double ClientTop => Location.Y + ActualBorderTopWidth + ActualPaddingTop;

        /// <summary>
        /// Gets the right of the client rectangle
        /// </summary>
        public double ClientRight => ActualRight - ActualPaddingRight - ActualBorderRightWidth;

        /// <summary>
        /// Gets the bottom of the client rectangle
        /// </summary>
        public double ClientBottom => ActualBottom - ActualPaddingBottom - ActualBorderBottomWidth;

        /// <summary>
        /// Gets the client rectangle
        /// </summary>
        public RRect ClientRectangle => RRect.FromLTRB(ClientLeft, ClientTop, ClientRight, ClientBottom);

        /// <summary>
        /// Gets the actual height
        /// </summary>
        public double ActualHeight => ActualBoxSizingHeight;

        /// <summary>
        /// Gets the actual height
        /// </summary>
        public double ActualWidth => ActualBoxSizingWidth;

        /// <summary>
        /// Gets the actual top's padding
        /// </summary>
        public double ActualPaddingTop
        {
            get
            {
                if (double.IsNaN(_actualPaddingTop))
                {
                    _actualPaddingTop = CssValueParser.ParseLength(PaddingTop, Size.Width, this);
                }
                return _actualPaddingTop;
            }
        }

        /// <summary>
        /// Gets the actual padding on the left
        /// </summary>
        public double ActualPaddingLeft
        {
            get
            {
                if (double.IsNaN(_actualPaddingLeft))
                {
                    _actualPaddingLeft = CssValueParser.ParseLength(PaddingLeft, Size.Width, this);
                }
                return _actualPaddingLeft;
            }
        }

        /// <summary>
        /// Gets the actual Padding of the bottom
        /// </summary>
        public double ActualPaddingBottom
        {
            get
            {
                if (double.IsNaN(_actualPaddingBottom))
                {
                    _actualPaddingBottom = CssValueParser.ParseLength(PaddingBottom, Size.Width, this);
                }
                return _actualPaddingBottom;
            }
        }

        /// <summary>
        /// Gets the actual padding on the right
        /// </summary>
        public double ActualPaddingRight
        {
            get
            {
                if (double.IsNaN(_actualPaddingRight))
                {
                    _actualPaddingRight = CssValueParser.ParseLength(PaddingRight, Size.Width, this);
                }
                return _actualPaddingRight;
            }
        }

        /// <summary>
        /// The margin top value if was effected by margin collapse.
        /// </summary>
        public double CollapsedMarginTop
        {
            get => double.IsNaN(_collapsedMarginTop) ? 0 : _collapsedMarginTop;
            set => _collapsedMarginTop = value;
        }

        /// <summary>
        /// Gets the actual top border width
        /// </summary>
        public double ActualBorderTopWidth
        {
            get
            {
                if (!double.IsNaN(_actualBorderTopWidth)) return _actualBorderTopWidth;

                _actualBorderTopWidth = CssValueParser.GetActualBorderWidth(BorderTopWidth, this);
                if (string.IsNullOrEmpty(BorderTopStyle) || BorderTopStyle == CssConstants.None)
                {
                    _actualBorderTopWidth = 0f;
                }
                return _actualBorderTopWidth;
            }
        }

        /// <summary>
        /// Gets the actual Left border width
        /// </summary>
        public double ActualBorderLeftWidth
        {
            get
            {
                if (!double.IsNaN(_actualBorderLeftWidth)) return _actualBorderLeftWidth;

                _actualBorderLeftWidth = CssValueParser.GetActualBorderWidth(BorderLeftWidth, this);
                if (string.IsNullOrEmpty(BorderLeftStyle) || BorderLeftStyle == CssConstants.None)
                {
                    _actualBorderLeftWidth = 0f;
                }
                return _actualBorderLeftWidth;
            }
        }

        /// <summary>
        /// Gets the actual Bottom border width
        /// </summary>
        public double ActualBorderBottomWidth
        {
            get
            {
                if (!double.IsNaN(_actualBorderBottomWidth)) return _actualBorderBottomWidth;

                _actualBorderBottomWidth = CssValueParser.GetActualBorderWidth(BorderBottomWidth, this);
                if (string.IsNullOrEmpty(BorderBottomStyle) || BorderBottomStyle == CssConstants.None)
                {
                    _actualBorderBottomWidth = 0f;
                }

                return _actualBorderBottomWidth;
            }
        }

        /// <summary>
        /// Gets the actual Right border width
        /// </summary>
        public double ActualBorderRightWidth
        {
            get
            {
                if (!double.IsNaN(_actualBorderRightWidth)) return _actualBorderRightWidth;

                _actualBorderRightWidth = CssValueParser.GetActualBorderWidth(BorderRightWidth, this);
                if (string.IsNullOrEmpty(BorderRightStyle) || BorderRightStyle == CssConstants.None)
                {
                    _actualBorderRightWidth = 0f;
                }

                return _actualBorderRightWidth;
            }
        }

        /// <summary>
        /// Gets the actual column-rule width (the line drawn between columns in a multi-column container)
        /// </summary>
        public double ActualColumnRuleWidth
        {
            get
            {
                if (!double.IsNaN(_actualColumnRuleWidth)) return _actualColumnRuleWidth;

                _actualColumnRuleWidth = CssValueParser.GetActualBorderWidth(ColumnRuleWidth, this);
                if (string.IsNullOrEmpty(ColumnRuleStyle) || ColumnRuleStyle == CssConstants.None)
                {
                    _actualColumnRuleWidth = 0f;
                }

                return _actualColumnRuleWidth;
            }
        }

        /// <summary>
        /// Gets the actual top border Color
        /// </summary>
        public RColor ActualBorderTopColor
        {
            get
            {
                if (_actualBorderTopColor.IsEmpty)
                {
                    _actualBorderTopColor = GetActualColor(BorderTopColor);
                }
                return _actualBorderTopColor;
            }
        }

        protected abstract RColor GetActualColor(string colorStr);

        /// <summary>
        /// Gets the actual Left border Color
        /// </summary>
        public RColor ActualBorderLeftColor
        {
            get
            {
                if ((_actualBorderLeftColor.IsEmpty))
                {
                    _actualBorderLeftColor = GetActualColor(BorderLeftColor);
                }
                return _actualBorderLeftColor;
            }
        }

        /// <summary>
        /// Gets the actual Bottom border Color
        /// </summary>
        public RColor ActualBorderBottomColor
        {
            get
            {
                if ((_actualBorderBottomColor.IsEmpty))
                {
                    _actualBorderBottomColor = GetActualColor(BorderBottomColor);
                }
                return _actualBorderBottomColor;
            }
        }

        /// <summary>
        /// Gets the actual Right border Color
        /// </summary>
        public RColor ActualBorderRightColor
        {
            get
            {
                if ((_actualBorderRightColor.IsEmpty))
                {
                    _actualBorderRightColor = GetActualColor(BorderRightColor);
                }
                return _actualBorderRightColor;
            }
        }

        /// <summary>
        /// Gets the actual column-rule color (the line drawn between columns in a multi-column container)
        /// </summary>
        public RColor ActualColumnRuleColor
        {
            get
            {
                if (_actualColumnRuleColor.IsEmpty)
                {
                    _actualColumnRuleColor = GetActualColor(ColumnRuleColor);
                }
                return _actualColumnRuleColor;
            }
        }

        public double ActualBorderTopLeftRadiusX
        {
            get
            {
                if (double.IsNaN(_actualBorderTopLeftRadiusX))
                    _actualBorderTopLeftRadiusX = CssValueParser.ParseLength(FirstCssValue(BorderTopLeftRadius), ActualBoxSizingWidth, this);
                return _actualBorderTopLeftRadiusX;
            }
        }

        public double ActualBorderTopLeftRadiusY
        {
            get
            {
                if (double.IsNaN(_actualBorderTopLeftRadiusY))
                    _actualBorderTopLeftRadiusY = CssValueParser.ParseLength(SecondCssValue(BorderTopLeftRadius), ActualBoxSizingHeight, this);
                return _actualBorderTopLeftRadiusY;
            }
        }

        public double ActualBorderTopRightRadiusX
        {
            get
            {
                if (double.IsNaN(_actualBorderTopRightRadiusX))
                    _actualBorderTopRightRadiusX = CssValueParser.ParseLength(FirstCssValue(BorderTopRightRadius), ActualBoxSizingWidth, this);
                return _actualBorderTopRightRadiusX;
            }
        }

        public double ActualBorderTopRightRadiusY
        {
            get
            {
                if (double.IsNaN(_actualBorderTopRightRadiusY))
                    _actualBorderTopRightRadiusY = CssValueParser.ParseLength(SecondCssValue(BorderTopRightRadius), ActualBoxSizingHeight, this);
                return _actualBorderTopRightRadiusY;
            }
        }

        public double ActualBorderBottomRightRadiusX
        {
            get
            {
                if (double.IsNaN(_actualBorderBottomRightRadiusX))
                    _actualBorderBottomRightRadiusX = CssValueParser.ParseLength(FirstCssValue(BorderBottomRightRadius), ActualBoxSizingWidth, this);
                return _actualBorderBottomRightRadiusX;
            }
        }

        public double ActualBorderBottomRightRadiusY
        {
            get
            {
                if (double.IsNaN(_actualBorderBottomRightRadiusY))
                    _actualBorderBottomRightRadiusY = CssValueParser.ParseLength(SecondCssValue(BorderBottomRightRadius), ActualBoxSizingHeight, this);
                return _actualBorderBottomRightRadiusY;
            }
        }

        public double ActualBorderBottomLeftRadiusX
        {
            get
            {
                if (double.IsNaN(_actualBorderBottomLeftRadiusX))
                    _actualBorderBottomLeftRadiusX = CssValueParser.ParseLength(FirstCssValue(BorderBottomLeftRadius), ActualBoxSizingWidth, this);
                return _actualBorderBottomLeftRadiusX;
            }
        }

        public double ActualBorderBottomLeftRadiusY
        {
            get
            {
                if (double.IsNaN(_actualBorderBottomLeftRadiusY))
                    _actualBorderBottomLeftRadiusY = CssValueParser.ParseLength(SecondCssValue(BorderBottomLeftRadius), ActualBoxSizingHeight, this);
                return _actualBorderBottomLeftRadiusY;
            }
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
            double tlX = ActualBorderTopLeftRadiusX,     tlY = ActualBorderTopLeftRadiusY;
            double trX = ActualBorderTopRightRadiusX,    trY = ActualBorderTopRightRadiusY;
            double brX = ActualBorderBottomRightRadiusX, brY = ActualBorderBottomRightRadiusY;
            double blX = ActualBorderBottomLeftRadiusX,  blY = ActualBorderBottomLeftRadiusY;

            // Horizontal reduction: check top side and bottom side independently.
            double fTop = tlX + trX > 0 && rect.Width > 0 ? rect.Width / (tlX + trX) : 1.0;
            double fBot = blX + brX > 0 && rect.Width > 0 ? rect.Width / (blX + brX) : 1.0;
            double fX = Math.Min(1.0, Math.Min(fTop, fBot));

            // Vertical reduction: check left side and right side independently.
            double fLeft  = tlY + blY > 0 && rect.Height > 0 ? rect.Height / (tlY + blY) : 1.0;
            double fRight = trY + brY > 0 && rect.Height > 0 ? rect.Height / (trY + brY) : 1.0;
            double fY = Math.Min(1.0, Math.Min(fLeft, fRight));

            return new BorderRadii(tlX * fX, tlY * fY, trX * fX, trY * fY,
                                   brX * fX, brY * fY, blX * fX, blY * fY);
        }

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
                    _actualTransformMatrix = CssValueParser.ParseTransform(Transform, TransformOrigin, this);
                    _actualTransformComputed = true;
                }
                return _actualTransformMatrix;
            }
        }

        /// <summary>
        /// True when this box has a non-identity CSS transform to apply at paint time.
        /// </summary>
        public bool IsTransformed => !ActualTransformMatrix.IsIdentity;

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
                    _actualOpacity = string.IsNullOrEmpty(Opacity)
                        ? 1.0
                        : Math.Clamp(CssValueParser.ParseNumber(Opacity, 1.0), 0.0, 1.0);
                    _actualOpacityComputed = true;
                }
                return _actualOpacity;
            }
        }

        /// <summary>
        /// True when this box's <c>opacity</c> is fully opaque (the common case) - false when a
        /// group-opacity transparency-group composite is needed at paint time.
        /// </summary>
        public bool IsOpaque => ActualOpacity >= 1.0;

        /// <summary>
        /// Gets a value indicating if at least one of the corners of the box is rounded.
        /// </summary>
        public bool IsRounded =>
            ActualBorderTopLeftRadiusX > 0 || ActualBorderTopLeftRadiusY > 0 ||
            ActualBorderTopRightRadiusX > 0 || ActualBorderTopRightRadiusY > 0 ||
            ActualBorderBottomRightRadiusX > 0 || ActualBorderBottomRightRadiusY > 0 ||
            ActualBorderBottomLeftRadiusX > 0 || ActualBorderBottomLeftRadiusY > 0;

        /// <summary>
        /// Gets the actual width of whitespace between words.
        /// </summary>
        public double ActualWordSpacing => _actualWordSpacing;

        /// <summary>
        /// 
        /// Gets the actual color for the text.
        /// </summary>
        public RColor ActualColor
        {
            get
            {
                if (_actualColor.IsEmpty)
                {
                    _actualColor = GetActualColor(Color);
                }

                return _actualColor;
            }
        }

        /// <summary>
        /// Gets the actual background color of the box
        /// </summary>
        public RColor ActualBackgroundColor
        {
            get
            {
                if (_actualBackgroundColor.IsEmpty)
                {
                    _actualBackgroundColor = GetActualColor(BackgroundColor);
                }

                return _actualBackgroundColor;
            }
        }

        /// <summary>
        /// Gets the font that should be actually used to paint the text of the box
        /// </summary>
        public RFont ActualFont
        {
            get
            {
                if (_actualFont != null) return _actualFont;

                if (string.IsNullOrEmpty(FontFamily))
                {
                    FontFamily = CssConstants.DefaultFont;
                }

                if (string.IsNullOrEmpty(FontSize))
                {
                    FontSize = CssConstants.FontSize.ToString(CultureInfo.InvariantCulture) + "pt";
                }

                var st = GetActualFontStyleFlags();

                double fsize;
                double parentSize = CssConstants.FontSize;
                double remSize;

                var parentBox = GetParent();

                if (parentBox is not null)
                {
                    parentSize = GetParent()!.ActualFont.Size;
                    remSize = GetRemHeight();
                }
                else
                {
                    remSize = CssConstants.FontSize;
                }

                fsize = FontSizeResolver.Resolve(FontSize, parentSize, remSize);

                _actualFont = GetCachedFont(FontFamily, fsize, st) ?? GetCachedFont(CssConstants.DefaultFont, fsize, st);

                if (_actualFont is null)
                {
                    throw new HtmlRenderException($"Cannot find font: {FontFamily} and Default Font {CssConstants.DefaultFont} is not installed", HtmlRenderErrorType.General);
                }

                return _actualFont!;
            }
        }

        /// <summary>
        /// Computes the <see cref="RFontStyle"/> flags (italic/bold) for this box's own
        /// <see cref="FontStyle"/>/<see cref="FontWeight"/> — shared between <see cref="ActualFont"/> and
        /// any derived font (e.g. a synthesized small-caps run) that needs the same style bits at a
        /// different size, so the two never drift apart.
        /// </summary>
        private RFontStyle GetActualFontStyleFlags()
        {
            var st = RFontStyle.Regular;

            if (FontStyle is CssConstants.Italic or CssConstants.Oblique)
            {
                st |= RFontStyle.Italic;
            }

            if (int.TryParse(FontWeight, out var fontWeightValue))
            {
                if (fontWeightValue >= 700)
                {
                    st |= RFontStyle.Bold;
                }
            }
            else if (FontWeight is CssConstants.Bold or CssConstants.Bolder)
            {
                st |= RFontStyle.Bold;
            }

            return st;
        }

        /// <summary>
        /// Size ratio applied to an originally-lowercase run when synthesizing
        /// <c>font-variant: small-caps</c> (see <see cref="ActualSmallCapsFont"/>, <see cref="CssRect.FontSizeScale"/>).
        /// Not derived from any real OpenType metric (PeachPDF has no shaping engine to measure a font's
        /// actual <c>smcp</c> cap-height) — a representative approximation, tuned by eye.
        /// </summary>
        public const double SmallCapsFontScale = 0.72;

        private RFont? _smallCapsFont;

        /// <summary>
        /// A cached font derived from <see cref="ActualFont"/> at a reduced size (same family/style),
        /// used to synthesize <c>font-variant: small-caps</c> — PeachPDF has no OpenType shaping engine
        /// to do real <c>smcp</c>/<c>c2sc</c> glyph substitution, so originally-lowercase runs are
        /// upper-cased and drawn at this smaller size instead. See <see cref="CssBox.ParseToWords"/>.
        /// </summary>
        public RFont ActualSmallCapsFont
        {
            get
            {
                if (_smallCapsFont != null) return _smallCapsFont;

                var font = ActualFont;
                _smallCapsFont = GetCachedFont(FontFamily!, font.Size * SmallCapsFontScale, GetActualFontStyleFlags())
                                 ?? font;
                return _smallCapsFont;
            }
        }

        protected abstract RFont? GetCachedFont(string fontFamily, double fsize, RFontStyle st);

        /// <summary>
        /// Gets the line height
        /// </summary>
        public double ActualLineHeight => LineHeight is CssConstants.Normal ? 1.2 * GetEmHeight() : CssValueParser.ParseLength(LineHeight, Size.Height, this);

        /// <summary>
        /// Gets the text indentation (on first line only)
        /// </summary>
        public double ActualTextIndent
        {
            get
            {
                if (double.IsNaN(_actualTextIndent))
                {
                    _actualTextIndent = CssValueParser.ParseLength(TextIndent, Size.Width, this);
                }

                return _actualTextIndent;
            }
        }

        /// <summary>
        /// Gets the actual horizontal border spacing for tables
        /// </summary>
        public double ActualBorderSpacingHorizontal
        {
            get
            {
                if (!double.IsNaN(_actualBorderSpacingHorizontal)) return _actualBorderSpacingHorizontal;

                // Paren-depth-aware split (not a naive regex length-search) so a calc()/min()/max()/clamp()
                // value's internal spaces aren't mistaken for the horizontal/vertical delimiter.
                var parts = new List<string>(CssValueParser.SplitTopLevelWhitespace(BorderSpacing));

                _actualBorderSpacingHorizontal = parts.Count > 0
                    ? CssValueParser.ParseLength(parts[0], 1, this)
                    : 0;

                return _actualBorderSpacingHorizontal;
            }
        }

        /// <summary>
        /// Gets the actual vertical border spacing for tables
        /// </summary>
        public double ActualBorderSpacingVertical
        {
            get
            {
                if (!double.IsNaN(_actualBorderSpacingVertical)) return _actualBorderSpacingVertical;

                var parts = new List<string>(CssValueParser.SplitTopLevelWhitespace(BorderSpacing));

                _actualBorderSpacingVertical = parts.Count switch
                {
                    0 => 0,
                    1 => CssValueParser.ParseLength(parts[0], 1, this),
                    _ => CssValueParser.ParseLength(parts[1], 1, this)
                };

                return _actualBorderSpacingVertical;
            }
        }

        /// <summary>
        /// Returns true if this is a positioned element, i.e., it has a position of
        /// relative, absolute, fixed, or sticky
        /// </summary>
        public bool IsPositioned => Position is CssConstants.Relative or CssConstants.Absolute or CssConstants.Fixed or CssConstants.Sticky;

        public string ZIndex { get; set; } = CssConstants.Auto;

        // Flex container properties
        public string FlexDirection  { get; set; } = "row";
        public string FlexWrap       { get; set; } = "nowrap";
        public string JustifyContent { get; set; } = "normal";
        public string AlignItems     { get; set; } = "normal";
        public string AlignContent   { get; set; } = "normal";

        // Flex item properties
        public string FlexGrow   { get; set; } = "0";
        public string FlexShrink { get; set; } = "1";
        public string FlexBasis  { get; set; } = "auto";
        public string AlignSelf      { get; set; } = "auto";
        public string Order          { get; set; } = "0";
        // Gap between flex items (column-gap = main-axis gap for row direction)
        public string FlexRowGap    { get; set; } = "0";
        public string FlexColumnGap { get; set; } = "0";

        // Multi-column layout container properties. column-gap itself is shared with flex/grid (the
        // same CSS property, see FlexColumnGap above); these are the multicol-only ones.
        public string ColumnCount     { get; set; } = CssConstants.Auto;
        public string ColumnWidth     { get; set; } = CssConstants.Auto;
        public string ColumnFill      { get; set; } = "balance";
        public string ColumnSpan      { get; set; } = CssConstants.None;
        public string ColumnRuleWidth { get; set; } = CssConstants.Medium;
        public string ColumnRuleStyle { get; set; } = CssConstants.None;
        public string ColumnRuleColor { get; set; } = CssConstants.CurrentColor;

        /// <summary>
        /// Whether this box establishes a CSS multi-column formatting context, per spec: <c>column-width</c>
        /// is not <c>auto</c>, or <c>column-count</c> is not <c>auto</c>.
        /// </summary>
        public bool EstablishesMultiColumnContext =>
            (!string.IsNullOrEmpty(ColumnCount) && ColumnCount != CssConstants.Auto) ||
            (!string.IsNullOrEmpty(ColumnWidth) && ColumnWidth != CssConstants.Auto);

        /// <summary>
        /// Get the parent of this css properties instance.
        /// </summary>
        /// <returns></returns>
        protected abstract CssBoxProperties? GetParent();

        /// <summary>
        /// Gets the size of 1em in the specified units, per spec: an element's own computed
        /// font-size, not the font's line-spacing metric (ascent+descent+leading), which is
        /// typically 15-30%+ larger and would inflate every em-based margin/padding/line-height.
        /// </summary>
        /// <returns></returns>
        public double GetEmHeight()
        {
            return ActualFont.Size;
        }

        /// <summary>
        /// Gets the height of the root font in the specified units
        /// </summary>
        /// <returns></returns>
        public double GetRemHeight()
        {
            var box = this;
            var parentBox = box.GetParent();

            while (parentBox is not null)
            {
                box = parentBox;
                parentBox = box.GetParent();
            }

            return box.GetEmHeight();
        }

        /// <summary>
        /// Ensures that the specified length is converted to pixels if necessary
        /// </summary>
        /// <param name="length"></param>
        protected string NoEms(string length)
        {
            var len = new CssLength(length);
            if (len.Unit == CssUnit.Ems)
            {
                length = len.ConvertEmToPixels(GetEmHeight()).ToString();
            }
            return length;
        }

        /// <summary>
        /// Set the style/width/color for all 4 borders on the box.<br/>
        /// if null is given for a value it will not be set.
        /// </summary>
        /// <param name="style">optional: the style to set</param>
        /// <param name="width">optional: the width to set</param>
        /// <param name="color">optional: the color to set</param>
        protected void SetAllBorders(string? style = null, string? width = null, string? color = null)
        {
            if (style != null)
                BorderLeftStyle = BorderTopStyle = BorderRightStyle = BorderBottomStyle = style;
            if (width != null)
                BorderLeftWidth = BorderTopWidth = BorderRightWidth = BorderBottomWidth = width;
            if (color != null)
                BorderLeftColor = BorderTopColor = BorderRightColor = BorderBottomColor = color;
        }

        /// <summary>
        /// Measures the width of whitespace between words (set <see cref="ActualWordSpacing"/>).
        /// </summary>
        protected void MeasureWordSpacing(RGraphics g)
        {
            if (!double.IsNaN(ActualWordSpacing)) return;

            _actualWordSpacing = CssUtils.WhiteSpace(g, this);
            if (WordSpacing == CssConstants.Normal) return;

            _actualWordSpacing += CssValueParser.ParseLength(WordSpacing, 1, this);
        }

        /// <summary>
        /// Inherits inheritable values from specified box.
        /// </summary>
        /// <param name="everything">Set to true to inherit all CSS properties instead of only the ineritables</param>
        /// <param name="p">Box to inherit the properties</param>
        protected void InheritStyle(CssBox? p, bool everything)
        {
            if (p == null) return;

            // Custom properties are always inherited, regardless of the `everything` special case.
            // Cloned (not shared) so a child's local override never mutates the parent's or a sibling's dictionary.
            CustomProperties = p.CustomProperties is { Count: > 0 }
                ? new Dictionary<string, string>(p.CustomProperties)
                : null;

            BorderSpacing = p.BorderSpacing;
            BorderCollapse = p.BorderCollapse;
            _color = p._color;
            EmptyCells = p.EmptyCells;
            WhiteSpace = p.WhiteSpace;
            Visibility = p.Visibility;
            _textIndent = p._textIndent;
            TextAlign = p.TextAlign;
            TextTransform = p.TextTransform;
            VerticalAlign = p.VerticalAlign;
            FontFamily = p.FontFamily;
            _fontSize = p._fontSize;
            FontStyle = p.FontStyle;
            FontVariant = p.FontVariant;
            FontWeight = p.FontWeight;
            ListStyleImage = p.ListStyleImage;
            ListStylePosition = p.ListStylePosition;
            ListStyleType = p.ListStyleType;
            LineHeight = p.LineHeight;
            WordBreak = p.WordBreak;
            Direction = p.Direction;
            BoxSizing = p.BoxSizing;
            Orphans = p.Orphans;
            Widows = p.Widows;
            Hyphens = p.Hyphens;

            if (!everything) return;

            BackgroundColor = p.BackgroundColor;
            BackgroundImages = p.BackgroundImages;
            BackgroundPosition = p.BackgroundPosition;
            BackgroundRepeat = p.BackgroundRepeat;
            BackgroundOrigin = p.BackgroundOrigin;
            BackgroundClip = p.BackgroundClip;
            _borderTopWidth = p._borderTopWidth;
            _borderRightWidth = p._borderRightWidth;
            _borderBottomWidth = p._borderBottomWidth;
            _borderLeftWidth = p._borderLeftWidth;
            _borderTopColor = p._borderTopColor;
            _borderRightColor = p._borderRightColor;
            _borderBottomColor = p._borderBottomColor;
            _borderLeftColor = p._borderLeftColor;
            BorderTopStyle = p.BorderTopStyle;
            BorderRightStyle = p.BorderRightStyle;
            BorderBottomStyle = p.BorderBottomStyle;
            BorderLeftStyle = p.BorderLeftStyle;
            _bottom = p._bottom;
            BorderTopLeftRadius = p.BorderTopLeftRadius;
            BorderTopRightRadius = p.BorderTopRightRadius;
            BorderBottomRightRadius = p.BorderBottomRightRadius;
            BorderBottomLeftRadius = p.BorderBottomLeftRadius;
            Transform = p.Transform;
            TransformOrigin = p.TransformOrigin;
            Opacity = p.Opacity;
            Display = p.Display;
            Float = p.Float;
            Height = p.Height;
            MaxHeight = p.MaxHeight;
            MarginBottom = p.MarginBottom;
            MarginLeft = p.MarginLeft;
            MarginRight = p.MarginRight;
            MarginTop = p.MarginTop;
            Left = p.Left;
            LineHeight = p.LineHeight;
            Overflow = p.Overflow;
            _paddingLeft = p._paddingLeft;
            _paddingBottom = p._paddingBottom;
            _paddingRight = p._paddingRight;
            _paddingTop = p._paddingTop;
            _right = p._right;
            TextDecoration = p.TextDecoration;
            TextDecorationLine = p.TextDecorationLine;
            TextDecorationStyle = p.TextDecorationStyle;
            TextDecorationColor = p.TextDecorationColor;
            Top = p.Top;
            Position = p.Position;
            Width = p.Width;
            MaxWidth = p.MaxWidth;
            MinWidth = p.MinWidth;
            _wordSpacing = p._wordSpacing;
        }
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