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
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Parse;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PeachPDF.Html.Core.Utils
{
    /// <summary>
    /// Utility method for handling CSS stuff.
    /// </summary>
    internal static class CssUtils
    {
        /// <summary>
        /// Gets the white space width of the specified box
        /// </summary>
        /// <param name="g"></param>
        /// <param name="box"></param>
        /// <returns></returns>
        public static double WhiteSpace(RGraphics g, CssBoxProperties box)
        {
            var w = box.ActualFont.GetWhitespaceWidth(g);

            if (!(string.IsNullOrEmpty(box.WordSpacing) || box.WordSpacing == CssConstants.Normal))
            {
                w += CssValueParser.ParseLength(box.WordSpacing, 0, box, true);
            }

            return w;
        }

        /// <summary>
        /// Get CSS box property value by the CSS name.<br/>
        /// Used as a mapping between CSS property and the class property.
        /// </summary>
        /// <param name="cssBox">the CSS box to get it's property value</param>
        /// <param name="propName">the name of the CSS property</param>
        /// <returns>the value of the property, null if no such property exists</returns>
        public static string? GetPropertyValue(CssBox cssBox, string propName)
        {
            return propName switch
            {
                "border-bottom-width" => cssBox.BorderBottomWidth,
                "border-left-width" => cssBox.BorderLeftWidth,
                "border-right-width" => cssBox.BorderRightWidth,
                "border-top-width" => cssBox.BorderTopWidth,
                "border-bottom-style" => cssBox.BorderBottomStyle,
                "border-left-style" => cssBox.BorderLeftStyle,
                "border-right-style" => cssBox.BorderRightStyle,
                "border-top-style" => cssBox.BorderTopStyle,
                "border-bottom-color" => cssBox.BorderBottomColor,
                "border-left-color" => cssBox.BorderLeftColor,
                "border-right-color" => cssBox.BorderRightColor,
                "border-top-color" => cssBox.BorderTopColor,
                "border-spacing" => cssBox.BorderSpacing,
                "border-collapse" => cssBox.BorderCollapse,
                "break-after" => cssBox.BreakAfter,
                "break-before" => cssBox.BreakBefore,
                "break-inside" => cssBox.BreakInside,
                "corner-radius" => cssBox.CornerRadius,
                "corner-nw-radius" => cssBox.CornerNwRadius,
                "corner-ne-radius" => cssBox.CornerNeRadius,
                "corner-se-radius" => cssBox.CornerSeRadius,
                "corner-sw-radius" => cssBox.CornerSwRadius,
                "counter-increment" => cssBox.CounterIncrement,
                "counter-reset" => cssBox.CounterReset,
                "counter-set" => cssBox.CounterSet,
                "string-set" => cssBox.StringSet,
                "margin-bottom" => cssBox.MarginBottom,
                "margin-left" => cssBox.MarginLeft,
                "margin-right" => cssBox.MarginRight,
                "margin-top" => cssBox.MarginTop,
                "padding-bottom" => cssBox.PaddingBottom,
                "padding-left" => cssBox.PaddingLeft,
                "padding-right" => cssBox.PaddingRight,
                "padding-top" => cssBox.PaddingTop,
                "page-break-after" => cssBox.BreakAfter,
                "page-break-before" => cssBox.BreakBefore,
                "page-break-inside" => cssBox.BreakInside,
                "left" => cssBox.Left,
                "top" => cssBox.Top,
                "right" => cssBox.Right,
                "bottom" => cssBox.Bottom,
                "width" => cssBox.Width,
                "max-width" => cssBox.MaxWidth,
                "height" => cssBox.Height,
                "min-height" => cssBox.MinHeight,
                "background-color" => cssBox.BackgroundColor,
                "background-image" => cssBox.BackgroundImage,
                "background-position" => cssBox.BackgroundPosition,
                "background-repeat" => cssBox.BackgroundRepeat,
                "background-gradient" => cssBox.BackgroundGradient,
                "background-gradient-angle" => cssBox.BackgroundGradientAngle,
                "content" => cssBox.Content,
                "color" => cssBox.Color,
                "display" => cssBox.Display,
                "direction" => cssBox.Direction,
                "empty-cells" => cssBox.EmptyCells,
                "float" => cssBox.Float,
                "clear" => cssBox.Clear,
                "position" => cssBox.Position,
                "line-height" => cssBox.LineHeight,
                "vertical-align" => cssBox.VerticalAlign,
                "text-indent" => cssBox.TextIndent,
                "text-align" => cssBox.TextAlign,
                "text-decoration" => cssBox.TextDecoration,
                "text-decoration-color" => cssBox.TextDecorationColor,
                "text-decoration-line" => cssBox.TextDecorationLine,
                "text-decoration-style" => cssBox.TextDecorationStyle,
                "white-space" => cssBox.WhiteSpace,
                "word-break" => cssBox.WordBreak,
                "visibility" => cssBox.Visibility,
                "word-spacing" => cssBox.WordSpacing,
                "font-family" => cssBox.FontFamily,
                "font-size" => cssBox.FontSize,
                "font-style" => cssBox.FontStyle,
                "font-variant" => cssBox.FontVariant,
                "font-weight" => cssBox.FontWeight,
                "list-style-position" => cssBox.ListStylePosition,
                "list-style-image" => cssBox.ListStyleImage,
                "list-style-type" => cssBox.ListStyleType,
                "overflow" => cssBox.Overflow,
                "z-index" => cssBox.ZIndex,
                _ => null
            };
        }

        /// <summary>
        /// Set CSS box property value by the CSS name.<br/>
        /// Used as a mapping between CSS property and the class property.
        /// </summary>
        /// <param name="valueParser">the css value parser to use</param>
        /// <param name="cssBox">the CSS box to set it's property value</param>
        /// <param name="propName">the name of the CSS property</param>
        /// <param name="value">the value to set</param>
        public static void SetPropertyValue(CssValueParser valueParser, CssBox cssBox, string propName, string value)
        {
            switch (propName)
            {
                case "border":
                    SetBorderPropertyValue(valueParser, cssBox, value, null);
                    break;
                case "border-bottom":
                    SetBorderPropertyValue(valueParser, cssBox, value, "bottom");
                    break;
                case "border-left":
                    SetBorderPropertyValue(valueParser, cssBox, value, "left");
                    break;
                case "border-right":
                    SetBorderPropertyValue(valueParser, cssBox, value, "right");
                    break;
                case "border-top":
                    SetBorderPropertyValue(valueParser, cssBox, value, "top");
                    break;
                case "border-width":
                    SetBorderChildPropertyValue(valueParser, cssBox, "width", value);
                    break;
                case "border-style":
                    SetBorderChildPropertyValue(valueParser, cssBox, "style", value);
                    break;
                case "border-color":
                    SetBorderChildPropertyValue(valueParser, cssBox, "color", value);
                    break;
                case "border-bottom-width":
                    if (IsValidLengthProperty(value))
                    {
                        cssBox.BorderBottomWidth = value;
                    }

                    break;
                case "border-left-width":
                    if (IsValidLengthProperty(value))
                    {
                        cssBox.BorderLeftWidth = value;
                    }

                    break;
                case "border-right-width":
                    if (IsValidLengthProperty(value))
                    {
                        cssBox.BorderRightWidth = value;
                    }

                    break;
                case "border-top-width":
                    if (IsValidLengthProperty(value))
                    {
                        cssBox.BorderTopWidth = value;
                    }

                    break;
                case "border-bottom-style":
                    if (IsValidBorderStyleProperty(value))
                    {
                        cssBox.BorderBottomStyle = value;
                    }

                    break;
                case "border-left-style":
                    if (IsValidBorderStyleProperty(value))
                    {
                        cssBox.BorderLeftStyle = value;
                    }

                    break;
                case "border-right-style":
                    if (IsValidBorderStyleProperty(value))
                    {
                        cssBox.BorderRightStyle = value;
                    }

                    break;
                case "border-top-style":
                    if (IsValidBorderStyleProperty(value))
                    {
                        cssBox.BorderTopStyle = value;
                    }

                    break;
                case "border-bottom-color":
                    if (IsValidColorProperty(valueParser, value))
                    {
                        cssBox.BorderBottomColor = value;
                    }

                    break;
                case "border-left-color":
                    if (IsValidColorProperty(valueParser, value))
                    {
                        cssBox.BorderLeftColor = value;
                    }

                    break;
                case "border-right-color":
                    if (IsValidColorProperty(valueParser, value))
                    {
                        cssBox.BorderRightColor = value;
                    }

                    break;
                case "border-top-color":
                    if (IsValidColorProperty(valueParser, value))
                    {
                        cssBox.BorderTopColor = value;
                    }

                    break;
                case "border-spacing":
                    cssBox.BorderSpacing = value;
                    break;
                case "border-collapse":
                    cssBox.BorderCollapse = value;
                    break;
                case "box-sizing":
                    if (IsValidBoxSizing(value))
                    {
                        cssBox.BoxSizing = value;
                    }

                    break;
                case "corner-radius":
                    cssBox.CornerRadius = value;
                    break;
                case "corner-nw-radius":
                    cssBox.CornerNwRadius = value;
                    break;
                case "corner-ne-radius":
                    cssBox.CornerNeRadius = value;
                    break;
                case "corner-se-radius":
                    cssBox.CornerSeRadius = value;
                    break;
                case "corner-sw-radius":
                    cssBox.CornerSwRadius = value;
                    break;
                case "counter-increment":
                    cssBox.CounterIncrement = value;
                    break;
                case "counter-reset":
                    cssBox.CounterReset = value;
                    break;
                case "counter-set":
                    cssBox.CounterSet = value;
                    break;
                case "string-set":
                    cssBox.StringSet = value;
                    break;
                case "margin":
                    SetMultiDirectionProperty(valueParser, cssBox, "margin", value);
                    break;
                case "margin-bottom":
                    cssBox.MarginBottom = value;
                    break;
                case "margin-left":
                    cssBox.MarginLeft = value;
                    break;
                case "margin-right":
                    cssBox.MarginRight = value;
                    break;
                case "margin-top":
                    cssBox.MarginTop = value;
                    break;
                case "padding":
                    SetMultiDirectionProperty(valueParser, cssBox, "padding", value);
                    break;
                case "padding-bottom":
                    cssBox.PaddingBottom = value;
                    break;
                case "padding-left":
                    cssBox.PaddingLeft = value;
                    break;
                case "padding-right":
                    cssBox.PaddingRight = value;
                    break;
                case "padding-top":
                    cssBox.PaddingTop = value;
                    break;
                case "break-after":
                case "page-break-after":
                    if (value is CssConstants.Always && propName is "page-break-after")
                    {
                        value = CssConstants.Page;
                    }

                    cssBox.BreakAfter = value;
                    break;
                case "page-break-before":
                case "break-before":
                    if (value is CssConstants.Always && propName is "page-break-before")
                    {
                        value = CssConstants.Page;
                    }

                    cssBox.BreakBefore = value;
                    break;
                case "page-break-inside":
                case "break-inside":
                    cssBox.BreakInside = value;
                    break;
                case "left":
                    cssBox.Left = value;
                    break;
                case "top":
                    cssBox.Top = value;
                    break;
                case "right":
                    cssBox.Right = value;
                    break;
                case "bottom":
                    cssBox.Bottom = value;
                    break;
                case "width":
                    if (IsValidLengthProperty(value))
                    {
                        cssBox.Width = value;
                    }

                    break;
                case "max-width":
                    if (IsValidLengthProperty(value))
                    {
                        cssBox.MaxWidth = value;
                    }

                    break;
                case "height":
                    if (IsValidLengthProperty(value))
                    {
                        cssBox.Height = value;
                    }

                    break;
                case "min-height":
                    if (IsValidLengthProperty(value))
                    {
                        cssBox.MinHeight = value;
                    }

                    break;
                case "background-color":
                    if (IsValidColorProperty(valueParser, value))
                    {
                        cssBox.BackgroundColor = value;
                    }

                    break;
                case "background-image":
                    cssBox.BackgroundImage = value;
                    break;
                case "background-position":
                    cssBox.BackgroundPosition = value;
                    break;
                case "background-repeat":
                    cssBox.BackgroundRepeat = value;
                    break;
                case "background-gradient":
                    cssBox.BackgroundGradient = value;
                    break;
                case "background-gradient-angle":
                    cssBox.BackgroundGradientAngle = value;
                    break;
                case "color":
                    if (IsValidColorProperty(valueParser, value))
                    {
                        cssBox.Color = value;
                    }

                    break;
                case "content":
                    cssBox.Content = value;
                    break;
                case "display":
                    cssBox.Display = value;
                    break;
                case "direction":
                    cssBox.Direction = value;
                    break;
                case "empty-cells":
                    cssBox.EmptyCells = value;
                    break;
                case "float":
                    cssBox.Float = value;
                    break;
                case "clear":
                    cssBox.Clear = value;
                    break;
                case "position":
                    cssBox.Position = value;
                    break;
                case "line-height":
                    if (IsValidLengthProperty(value))
                    {
                        cssBox.LineHeight = value;
                    }

                    break;
                case "vertical-align":
                    cssBox.VerticalAlign = value;
                    break;
                case "text-indent":
                    cssBox.TextIndent = value;
                    break;
                case "text-align":
                    cssBox.TextAlign = value;
                    break;
                case "text-decoration":
                    cssBox.TextDecoration = value;
                    break;
                case "text-decoration-color":
                    cssBox.TextDecorationColor = value;
                    break;
                case "text-decoration-line":
                    cssBox.TextDecorationLine = value;
                    break;
                case "text-decoration-style":
                    cssBox.TextDecorationStyle = value;
                    break;
                case "white-space":
                    cssBox.WhiteSpace = value;
                    break;
                case "word-break":
                    cssBox.WordBreak = value;
                    break;
                case "visibility":
                    cssBox.Visibility = value;
                    break;
                case "word-spacing":
                    cssBox.WordSpacing = value;
                    break;
                case "font":
                    SetFontPropertyValue(valueParser, cssBox, value);
                    break;
                case "font-family":
                    cssBox.FontFamily = valueParser.GetFontFamilyByName(value);
                    break;
                case "font-size":
                    cssBox.FontSize = value;
                    break;
                case "font-style":
                    cssBox.FontStyle = value;
                    break;
                case "font-variant":
                    cssBox.FontVariant = value;
                    break;
                case "font-weight":
                    cssBox.FontWeight = value;
                    break;
                case "list-style":
                    SetListStyle(cssBox, value);
                    break;
                case "list-style-position":
                    cssBox.ListStylePosition = value;
                    break;
                case "list-style-image":
                    cssBox.ListStyleImage = value;
                    break;
                case "list-style-type":
                    cssBox.ListStyleType = value;
                    break;
                case "overflow":
                    cssBox.Overflow = value;
                    break;
                case "z-index":
                    if (value is CssConstants.Auto || int.TryParse(value, out _))
                    {
                        cssBox.ZIndex = value;
                    }

                    break;
                case "unicode-bidi":
                case "background-attachment":
                case "background-clip":
                case "overflow-wrap":
                    break;
            }
        }

        public static void ApplyCurrentColor(CssBox box, CssValueParser valueParser)
        {
            string[] colorProperties =
            [
                "border-top-color",
                "border-bottom-color",
                "border-left-color",
                "border-right-color",
                "background-color"
            ];

            var colorValue = GetPropertyValue(box, "color") ?? CssConstants.Initial;

            foreach (var propertyName in colorProperties)
            {
                var value = GetPropertyValue(box, propertyName);

                if (value is CssConstants.CurrentColor)
                {
                    SetPropertyValue(valueParser, box, propertyName, colorValue);
                }
            }
        }

        private static bool IsValidLengthProperty(string propValue)
        {
            return CssValueParser.IsValidLength(propValue) ||
                   propValue.Equals(CssConstants.Auto, StringComparison.InvariantCultureIgnoreCase);
        }

        private static bool IsValidColorProperty(CssValueParser valueParser, string propValue)
        {
            return propValue is CssConstants.CurrentColor || valueParser.IsColorValid(propValue);
        }

        private static bool IsValidBorderStyleProperty(string propValue)
        {
            return propValue switch
            {
                CssConstants.None => true,
                CssConstants.Solid => true,
                CssConstants.Hidden => true,
                CssConstants.Dotted => true,
                CssConstants.Dashed => true,
                CssConstants.Double => true,
                CssConstants.Groove => true,
                CssConstants.Ridge => true,
                CssConstants.Inset => true,
                CssConstants.Outset => true,
                _ => false
            };
        }

        public static bool IsValidBoxSizing(string propValue)
        {
            return propValue is CssConstants.BorderBox or CssConstants.ContentBox;
        }

        private static void SetFontPropertyValue(CssValueParser valueParser, CssBox box, string propValue)
        {
            var mustBe =
                RegexParserUtils.Search(RegexParserUtils.CssFontSizeAndLineHeight, propValue, out var mustBePos);

            if (string.IsNullOrEmpty(mustBe)) return;

            mustBe = mustBe.Trim();
            //Check for style||variant||weight on the left
            var leftSide = propValue[..mustBePos];
            var fontStyle = RegexParserUtils.Search(RegexParserUtils.CssFontStyle, leftSide);
            var fontVariant = RegexParserUtils.Search(RegexParserUtils.CssFontVariant, leftSide);
            var fontWeight = RegexParserUtils.Search(RegexParserUtils.CssFontWeight, leftSide);

            //Check for family on the right
            var rightSide = propValue[(mustBePos + mustBe.Length)..];
            var fontFamily =
                rightSide.Trim(); //Parser.Search(Parser.CssFontFamily, rightSide); //TODO: Would this be right?

            //Check for font-size and line-height
            var fontSize = mustBe;
            var lineHeight = string.Empty;

            if (mustBe.Contains('/') && mustBe.Length > mustBe.IndexOf('/') + 1)
            {
                var slashPos = mustBe.IndexOf('/');
                fontSize = mustBe[..slashPos];
                lineHeight = mustBe[(slashPos + 1)..];
            }

            if (!string.IsNullOrEmpty(fontFamily))
                SetPropertyValue(valueParser, box, "font-family", valueParser.GetFontFamilyByName(fontFamily));

            if (!string.IsNullOrEmpty(fontStyle))
                SetPropertyValue(valueParser, box, "font-style", fontStyle);

            if (!string.IsNullOrEmpty(fontVariant))
                SetPropertyValue(valueParser, box, "font-variant", fontVariant);

            if (!string.IsNullOrEmpty(fontWeight))
                SetPropertyValue(valueParser, box, "font-weight", fontWeight);

            if (!string.IsNullOrEmpty(fontSize))
                SetPropertyValue(valueParser, box, "font-size", fontSize);

            if (!string.IsNullOrEmpty(lineHeight))
                SetPropertyValue(valueParser, box, "line-height", lineHeight);

            // Check for: caption | icon | menu | message-box | small-caption | status-bar
            //TODO: Interpret font values of: caption | icon | menu | message-box | small-caption | status-bar
        }

        private static void SetBorderPropertyValue(CssValueParser valueParser, CssBox box, string propValue, string? direction)
        {
            ParseBorder(valueParser, propValue, out var borderWidth, out var borderStyle, out var borderColor);

            var borderDirectionPropertyName = "border";

            if (direction is not null)
            {
                borderDirectionPropertyName += "-" + direction;
            }

            if (borderWidth is not null)
            {
                SetPropertyValue(valueParser, box, borderDirectionPropertyName + "-width", borderWidth);
            }

            if (borderStyle is not null)
            {
                SetPropertyValue(valueParser, box, borderDirectionPropertyName + "-style", borderStyle);
            }

            if (borderColor is not null)
            {
                SetPropertyValue(valueParser, box, borderDirectionPropertyName + "-color", borderColor);
            }
        }

        private static void SetBorderChildPropertyValue(CssValueParser valueParser, CssBox box, string borderChildProperty, string propValue)
        {
            SplitMultiDirectionValues(propValue, out var left, out var top, out var right, out var bottom);

            if (left is not null)
            {
                SetPropertyValue(valueParser, box, $"border-left-{borderChildProperty}", left);
            }

            if (top is not null)
            {
                SetPropertyValue(valueParser, box, $"border-top-{borderChildProperty}", top);
            }

            if (right is not null)
            {
                SetPropertyValue(valueParser, box, $"border-right-{borderChildProperty}", right);
            }

            if (bottom is not null)
            {
                SetPropertyValue(valueParser, box, $"border-bottom-{borderChildProperty}", bottom);
            }
        }

        private static void SetMultiDirectionProperty(CssValueParser valueParser, CssBox box, string basePropertyName, string propValue)
        {
            SplitMultiDirectionValues(propValue, out var left, out var top, out var right, out var bottom);

            if (left is not null)
            {
                SetPropertyValue(valueParser, box, $"{basePropertyName}-left", left);
            }

            if (top is not null)
            {
                SetPropertyValue(valueParser, box, $"{basePropertyName}-top", top);
            }

            if (right is not null)
            {
                SetPropertyValue(valueParser, box, $"{basePropertyName}-right", right);
            }

            if (bottom is not null)
            {
                SetPropertyValue(valueParser, box, $"{basePropertyName}-bottom", bottom);
            }
        }

        /// <summary>
        /// Split multi direction value into the proper direction values (left, top, right, bottom).
        /// </summary>
        private static void SplitMultiDirectionValues(string propValue, out string? left, out string? top, out string? right, out string? bottom)
        {
            top = null;
            left = null;
            right = null;
            bottom = null;

            var values = SplitValues(propValue).ToArray();

            switch (values.Length)
            {
                case 1:
                    top = left = right = bottom = values[0];
                    break;
                case 2:
                    top = bottom = values[0];
                    left = right = values[1];
                    break;
                case 3:
                    top = values[0];
                    left = right = values[1];
                    bottom = values[2];
                    break;
                case 4:
                    top = values[0];
                    right = values[1];
                    bottom = values[2];
                    left = values[3];
                    break;
            }
        }

        private static void SetListStyle(CssBox box, string propValue)
        {
            var values = SplitValues(propValue);

            var listStyleType = CssConstants.None;
            var listStyleImage = CssConstants.None;
            var listStylePosition = CssConstants.Outside;

            foreach (var value in values)
            {
                if (value is CssConstants.Inside or CssConstants.Outside)
                {
                    listStylePosition = value;
                }

                var imageValue = CssValueParser.GetImagePropertyValue(value);

                if (imageValue is null)
                {
                    listStyleType = value;
                }
                else
                {
                    listStyleImage = value;
                }
            }

            box.ListStyleType = listStyleType;
            box.ListStyleImage = listStyleImage;
            box.ListStylePosition = listStylePosition;
        }


        /// <summary>
        /// Split the value by the specified separator; e.g. Useful in values like 'padding:5 4 3 inherit'
        /// </summary>
        /// <param name="value">Value to be splitted</param>
        /// <param name="separator"> </param>
        /// <returns>Splitted and trimmed values</returns>
        private static IEnumerable<string> SplitValues(string value, char separator = ' ')
        {
            //TODO: CRITICAL! Don't split values on parenthesis (like rgb(0, 0, 0)) or quotes ("strings")

            if (string.IsNullOrEmpty(value)) yield break;
            var values = value.Split(separator);

            foreach (var t in values)
            {
                var val = t.Trim();

                if (!string.IsNullOrEmpty(val))
                {
                    yield return val;
                }
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <parm name="valueParser"></parm>
        /// <param name="value"></param>
        /// <param name="width"> </param>
        /// <param name="style"></param>
        /// <param name="color"></param>
        private static void ParseBorder(CssValueParser valueParser, string value, out string? width, out string? style, out string? color)
        {
            width = style = color = null;
            if (string.IsNullOrEmpty(value)) return;

            var idx = 0;
            while ((idx = CommonUtils.GetNextSubString(value, idx, out var length)) > -1)
            {
                width ??= ParseBorderWidth(value, idx, length);
                style ??= ParseBorderStyle(value, idx, length);
                color ??= ParseBorderColor(valueParser, value, idx, length);

                idx = idx + length + 1;
            }
        }

        /// <summary>
        /// Parse the given substring to extract border width substring.
        /// Assume given substring is not empty and all indexes are valid!<br/>
        /// </summary>
        /// <returns>found border width value or null</returns>
        private static string? ParseBorderWidth(string str, int idx, int length)
        {
            if ((length > 2 && char.IsDigit(str[idx])) || (length > 3 && str[idx] == '.'))
            {
                string? unit = null;
                if (CommonUtils.SubStringEquals(str, idx + length - 2, 2, CssConstants.Px))
                    unit = CssConstants.Px;
                else if (CommonUtils.SubStringEquals(str, idx + length - 2, 2, CssConstants.Pt))
                    unit = CssConstants.Pt;
                else if (CommonUtils.SubStringEquals(str, idx + length - 2, 2, CssConstants.Em))
                    unit = CssConstants.Em;
                else if (CommonUtils.SubStringEquals(str, idx + length - 2, 2, CssConstants.Ex))
                    unit = CssConstants.Ex;
                else if (CommonUtils.SubStringEquals(str, idx + length - 2, 2, CssConstants.In))
                    unit = CssConstants.In;
                else if (CommonUtils.SubStringEquals(str, idx + length - 2, 2, CssConstants.Cm))
                    unit = CssConstants.Cm;
                else if (CommonUtils.SubStringEquals(str, idx + length - 2, 2, CssConstants.Mm))
                    unit = CssConstants.Mm;
                else if (CommonUtils.SubStringEquals(str, idx + length - 2, 2, CssConstants.Pc))
                    unit = CssConstants.Pc;

                if (unit == null) return null;
                if (CssValueParser.IsFloat(str, idx, length - 2))
                    return str.Substring(idx, length);
            }
            else
            {
                if (CommonUtils.SubStringEquals(str, idx, length, CssConstants.Thin))
                    return CssConstants.Thin;
                if (CommonUtils.SubStringEquals(str, idx, length, CssConstants.Medium))
                    return CssConstants.Medium;
                if (CommonUtils.SubStringEquals(str, idx, length, CssConstants.Thick))
                    return CssConstants.Thick;
            }
            return null;
        }

        /// <summary>
        /// Parse the given substring to extract border style substring.<br/>
        /// Assume given substring is not empty and all indexes are valid!<br/>
        /// </summary>
        /// <returns>found border width value or null</returns>
        private static string? ParseBorderStyle(string str, int idx, int length)
        {
            if (CommonUtils.SubStringEquals(str, idx, length, CssConstants.None))
                return CssConstants.None;
            if (CommonUtils.SubStringEquals(str, idx, length, CssConstants.Solid))
                return CssConstants.Solid;
            if (CommonUtils.SubStringEquals(str, idx, length, CssConstants.Hidden))
                return CssConstants.Hidden;
            if (CommonUtils.SubStringEquals(str, idx, length, CssConstants.Dotted))
                return CssConstants.Dotted;
            if (CommonUtils.SubStringEquals(str, idx, length, CssConstants.Dashed))
                return CssConstants.Dashed;
            if (CommonUtils.SubStringEquals(str, idx, length, CssConstants.Double))
                return CssConstants.Double;
            if (CommonUtils.SubStringEquals(str, idx, length, CssConstants.Groove))
                return CssConstants.Groove;
            if (CommonUtils.SubStringEquals(str, idx, length, CssConstants.Ridge))
                return CssConstants.Ridge;
            if (CommonUtils.SubStringEquals(str, idx, length, CssConstants.Inset))
                return CssConstants.Inset;
            if (CommonUtils.SubStringEquals(str, idx, length, CssConstants.Outset))
                return CssConstants.Outset;
            return null;
        }

        /// <summary>
        /// Parse the given substring to extract border style substring.<br/>
        /// Assume given substring is not empty and all indexes are valid!<br/>
        /// </summary>
        /// <returns>found border width value or null</returns>
        private static string? ParseBorderColor(CssValueParser valueParser, string str, int idx, int length)
        {
            return valueParser.TryGetColor(str, idx, length, out _) ? str.Substring(idx, length) : null;
        }
    }
}