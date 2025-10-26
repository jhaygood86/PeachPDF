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

using PeachPDF.Html.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core.Entities;
using PeachPDF.Html.Core.Parse;
using PeachPDF.Html.Core.Utils;
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
        private string _cornerRadius = "0";
        private string _fontSize = "medium";
        private string _paddingLeft = "0";
        private string _paddingBottom = "0";
        private string _paddingRight = "0";
        private string _paddingTop = "0";
        private string? _right;
        private string _textIndent = "0";
        private string _wordSpacing = "normal";

        #endregion


        #region Fields

        private double _actualCornerNw = double.NaN;
        private double _actualCornerNe = double.NaN;
        private double _actualCornerSw = double.NaN;
        private double _actualCornerSe = double.NaN;
        private RColor _actualColor = RColor.Empty;
        private double _actualBackgroundGradientAngle = double.NaN;
        private double _actualPaddingTop = double.NaN;
        private double _actualPaddingBottom = double.NaN;
        private double _actualPaddingRight = double.NaN;
        private double _actualPaddingLeft = double.NaN;
        private double _collapsedMarginTop = double.NaN;
        private double _actualBorderTopWidth = double.NaN;
        private double _actualBorderLeftWidth = double.NaN;
        private double _actualBorderBottomWidth = double.NaN;
        private double _actualBorderRightWidth = double.NaN;

        /// <summary>
        /// the width of whitespace between words
        /// </summary>
        private double _actualWordSpacing = double.NaN;
        private double _actualTextIndent = double.NaN;
        private double _actualBorderSpacingHorizontal = double.NaN;
        private double _actualBorderSpacingVertical = double.NaN;
        private RColor _actualBackgroundGradient = RColor.Empty;
        private RColor _actualBorderTopColor = RColor.Empty;
        private RColor _actualBorderLeftColor = RColor.Empty;
        private RColor _actualBorderBottomColor = RColor.Empty;
        private RColor _actualBorderRightColor = RColor.Empty;
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

        public string CornerRadius
        {
            get => _cornerRadius;
            set
            {
                var r = RegexParserUtils.CssLengthRegex().Matches(value);

                switch (r.Count)
                {
                    case 1:
                        CornerNeRadius = r[0].Value;
                        CornerNwRadius = r[0].Value;
                        CornerSeRadius = r[0].Value;
                        CornerSwRadius = r[0].Value;
                        break;
                    case 2:
                        CornerNeRadius = r[0].Value;
                        CornerNwRadius = r[0].Value;
                        CornerSeRadius = r[1].Value;
                        CornerSwRadius = r[1].Value;
                        break;
                    case 3:
                        CornerNeRadius = r[0].Value;
                        CornerNwRadius = r[1].Value;
                        CornerSeRadius = r[2].Value;
                        break;
                    case 4:
                        CornerNeRadius = r[0].Value;
                        CornerNwRadius = r[1].Value;
                        CornerSeRadius = r[2].Value;
                        CornerSwRadius = r[3].Value;
                        break;
                }

                _cornerRadius = value;
            }
        }

        public string CornerNwRadius { get; set; } = "0";

        public string CornerNeRadius { get; set; } = "0";

        public string CornerSeRadius { get; set; } = "0";

        public string CornerSwRadius { get; set; } = "0";
        public string CounterIncrement { get; set; } = CssConstants.None;
        public string CounterReset { get; set; } = CssConstants.None;
        public string CounterSet { get; set; } = CssConstants.None;
        public string StringSet { get; set; } = CssConstants.None;

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

        public string Left { get; set; } = CssConstants.Auto;

        public string Top { get; set; } = CssConstants.Auto;

        public string Bottom { get; set; } = CssConstants.Auto;

        public string Right { get; set; } = CssConstants.Auto;

        public string Width { get; set; } = "auto";

        public string MaxWidth { get; set; } = "none";

        public string Height { get; set; } = "auto";
        public string MinHeight { get; set; } = "auto";

        public string BackgroundColor { get; set; } = "transparent";

        public string BackgroundImage { get; set; } = "none";

        public string BackgroundPosition { get; set; } = "0% 0%";

        public string BackgroundRepeat { get; set; } = "repeat";

        public string BackgroundGradient { get; set; } = "none";

        public string BackgroundGradientAngle { get; set; } = "90";

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

        public string WhiteSpace { get; set; } = "normal";

        public string Visibility { get; set; } = "visible";

        public string WordSpacing
        {
            get => _wordSpacing;
            set => _wordSpacing = NoEms(value);
        }

        public string WordBreak { get; set; } = "normal";

        public string? FontFamily { get; set; }

        public string FontSize
        {
            get => _fontSize;
            set
            {
                var length = RegexParserUtils.Search(RegexParserUtils.CssLengthRegex(), value);

                if (length != null)
                {
                    string computedValue;
                    CssLength len = new(length);

                    if (len.HasError)
                    {
                        computedValue = "medium";
                    }
                    else if (len.Unit == CssUnit.Ems && GetParent() != null)
                    {
                        computedValue = len.ConvertEmToPoints(GetParent()!.ActualFont.Size).ToString();
                    }
                    else
                    {
                        computedValue = len.ToString();
                    }

                    _fontSize = computedValue;
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

        public string ListStyleImage { get; set; } = string.Empty;

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
        /// Gets the actual length of the north west corner
        /// </summary>
        public double ActualCornerNw
        {
            get
            {
                if (double.IsNaN(_actualCornerNw))
                {
                    _actualCornerNw = CssValueParser.ParseLength(CornerNwRadius, 0, this);
                }
                return _actualCornerNw;
            }
        }

        /// <summary>
        /// Gets the actual length of the north east corner
        /// </summary>
        public double ActualCornerNe
        {
            get
            {
                if (double.IsNaN(_actualCornerNe))
                {
                    _actualCornerNe = CssValueParser.ParseLength(CornerNeRadius, 0, this);
                }
                return _actualCornerNe;
            }
        }

        /// <summary>
        /// Gets the actual length of the south east corner
        /// </summary>
        public double ActualCornerSe
        {
            get
            {
                if (double.IsNaN(_actualCornerSe))
                {
                    _actualCornerSe = CssValueParser.ParseLength(CornerSeRadius, 0, this);
                }
                return _actualCornerSe;
            }
        }

        /// <summary>
        /// Gets the actual length of the south west corner
        /// </summary>
        public double ActualCornerSw
        {
            get
            {
                if (double.IsNaN(_actualCornerSw))
                {
                    _actualCornerSw = CssValueParser.ParseLength(CornerSwRadius, 0, this);
                }
                return _actualCornerSw;
            }
        }

        /// <summary>
        /// Gets a value indicating if at least one of the corners of the box is rounded
        /// </summary>
        public bool IsRounded => ActualCornerNe > 0f || ActualCornerNw > 0f || ActualCornerSe > 0f || ActualCornerSw > 0f;

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
        /// Gets the second color that creates a gradient for the background
        /// </summary>
        public RColor ActualBackgroundGradient
        {
            get
            {
                if (_actualBackgroundGradient.IsEmpty)
                {
                    _actualBackgroundGradient = GetActualColor(BackgroundGradient);
                }
                return _actualBackgroundGradient;
            }
        }

        /// <summary>
        /// Gets the actual angle specified for the background gradient
        /// </summary>
        public double ActualBackgroundGradientAngle
        {
            get
            {
                if (double.IsNaN(_actualBackgroundGradientAngle))
                {
                    _actualBackgroundGradientAngle = CssValueParser.ParseNumber(BackgroundGradientAngle, 360f);
                }

                return _actualBackgroundGradientAngle;
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

                RFontStyle st = RFontStyle.Regular;

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

                double fsize;
                double parentSize = CssConstants.FontSize;
                double rootSize = CssConstants.FontSize;
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

                fsize = FontSize switch
                {
                    CssConstants.Medium => CssConstants.FontSize,
                    CssConstants.XXSmall => CssConstants.FontSize - 4,
                    CssConstants.XSmall => CssConstants.FontSize - 3,
                    CssConstants.Small => CssConstants.FontSize - 2,
                    CssConstants.Large => CssConstants.FontSize + 2,
                    CssConstants.XLarge => CssConstants.FontSize + 3,
                    CssConstants.XXLarge => CssConstants.FontSize + 4,
                    CssConstants.Smaller => parentSize - 2,
                    CssConstants.Larger => parentSize + 2,
                    _ => CssValueParser.ParseLength(FontSize, parentSize, parentSize, remSize, null, true, true)
                };

                if (fsize <= 1f)
                {
                    fsize = CssConstants.FontSize;
                }

                _actualFont = GetCachedFont(FontFamily, fsize, st) ?? GetCachedFont(CssConstants.DefaultFont, fsize, st);

                if (_actualFont is null)
                {
                    throw new HtmlRenderException($"Cannot find font: {FontFamily} and Default Font {CssConstants.DefaultFont} is not installed", HtmlRenderErrorType.General);
                }

                return _actualFont!;
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

                var matches = RegexParserUtils.CssLengthRegex().Matches(BorderSpacing);

                _actualBorderSpacingHorizontal = matches.Count switch
                {
                    0 => 0,
                    > 0 => CssValueParser.ParseLength(matches[0].Value, 1, this),
                    _ => _actualBorderSpacingHorizontal
                };

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
                var matches = RegexParserUtils.CssLengthRegex().Matches(BorderSpacing);

                _actualBorderSpacingVertical = matches.Count switch
                {
                    0 => 0,
                    1 => CssValueParser.ParseLength(matches[0].Value, 1, this),
                    _ => CssValueParser.ParseLength(matches[1].Value, 1, this)
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

        /// <summary>
        /// Get the parent of this css properties instance.
        /// </summary>
        /// <returns></returns>
        protected abstract CssBoxProperties? GetParent();

        /// <summary>
        /// Gets the height of the font in the specified units
        /// </summary>
        /// <returns></returns>
        public double GetEmHeight()
        {
            return ActualFont.Height;
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

            var len = RegexParserUtils.Search(RegexParserUtils.CssLengthRegex(), WordSpacing);
            _actualWordSpacing += CssValueParser.ParseLength(len!, 1, this);
        }

        /// <summary>
        /// Inherits inheritable values from specified box.
        /// </summary>
        /// <param name="everything">Set to true to inherit all CSS properties instead of only the ineritables</param>
        /// <param name="p">Box to inherit the properties</param>
        protected void InheritStyle(CssBox? p, bool everything)
        {
            if (p == null) return;

            BorderSpacing = p.BorderSpacing;
            BorderCollapse = p.BorderCollapse;
            _color = p._color;
            EmptyCells = p.EmptyCells;
            WhiteSpace = p.WhiteSpace;
            Visibility = p.Visibility;
            _textIndent = p._textIndent;
            TextAlign = p.TextAlign;
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

            if (!everything) return;

            BackgroundColor = p.BackgroundColor;
            BackgroundGradient = p.BackgroundGradient;
            BackgroundGradientAngle = p.BackgroundGradientAngle;
            BackgroundImage = p.BackgroundImage;
            BackgroundPosition = p.BackgroundPosition;
            BackgroundRepeat = p.BackgroundRepeat;
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
            CornerNwRadius = p.CornerNwRadius;
            CornerNeRadius = p.CornerNeRadius;
            CornerSeRadius = p.CornerSeRadius;
            CornerSwRadius = p.CornerSwRadius;
            _cornerRadius = p._cornerRadius;
            Display = p.Display;
            Float = p.Float;
            Height = p.Height;
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
            _wordSpacing = p._wordSpacing;
        }
    }
}