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
using PeachPDF.Html.Core.Entities;
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
                "border-top-left-radius" => cssBox.BorderTopLeftRadius,
                "border-top-right-radius" => cssBox.BorderTopRightRadius,
                "border-bottom-right-radius" => cssBox.BorderBottomRightRadius,
                "border-bottom-left-radius" => cssBox.BorderBottomLeftRadius,
                "counter-increment" => cssBox.CounterIncrement,
                "counter-reset" => cssBox.CounterReset,
                "counter-set" => cssBox.CounterSet,
                "string-set" => cssBox.StringSet,
                "page" => cssBox.PageName,
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
                "min-width" => cssBox.MinWidth,
                "height" => cssBox.Height,
                "max-height" => cssBox.MaxHeight,
                "min-height" => cssBox.MinHeight,
                "transform" => cssBox.Transform,
                "transform-origin" => cssBox.TransformOrigin,
                "background-color" => cssBox.BackgroundColor,
                "background-image" => null,
                "background-position" => cssBox.BackgroundPosition,
                "background-repeat" => cssBox.BackgroundRepeat,
                "background-origin" => cssBox.BackgroundOrigin,
                "background-clip" => cssBox.BackgroundClip,
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
                "list-style-image" => null,
                "list-style-type" => cssBox.ListStyleType,
                "overflow" => cssBox.Overflow,
                "z-index" => cssBox.ZIndex,
                "flex-direction"  => cssBox.FlexDirection,
                "flex-wrap"       => cssBox.FlexWrap,
                "justify-content" => cssBox.JustifyContent,
                "align-items"     => cssBox.AlignItems,
                "align-content"   => cssBox.AlignContent,
                "flex-grow"       => cssBox.FlexGrow,
                "flex-shrink"     => cssBox.FlexShrink,
                "flex-basis"      => cssBox.FlexBasis,
                "align-self"      => cssBox.AlignSelf,
                "order"           => cssBox.Order,
                "row-gap"         => cssBox.FlexRowGap,
                "column-gap"      => cssBox.FlexColumnGap,
                _ => null
            };
        }

        private static readonly string[] _knownPropertyNames =
        [
            "background-attachment", "background-clip", "background-color", "background-image",
            "background-origin", "background-position", "background-repeat", "background-size",
            "border-bottom-color", "border-bottom-style", "border-bottom-width",
            "border-bottom-left-radius", "border-bottom-right-radius",
            "border-collapse",
            "border-left-color", "border-left-style", "border-left-width",
            "border-right-color", "border-right-style", "border-right-width",
            "border-spacing",
            "border-top-color", "border-top-style", "border-top-width",
            "border-top-left-radius", "border-top-right-radius",
            "bottom", "box-sizing",
            "break-after", "break-before", "break-inside",
            "clear", "color", "content",
            "counter-increment", "counter-reset", "counter-set",
            "direction", "display",
            "empty-cells", "float",
            "font-family", "font-size", "font-style", "font-variant", "font-weight",
            "height",
            "left", "line-height",
            "list-style-image", "list-style-position", "list-style-type",
            "margin-bottom", "margin-left", "margin-right", "margin-top",
            "max-width", "min-width", "max-height", "min-height",
            "overflow",
            "padding-bottom", "padding-left", "padding-right", "padding-top",
            "position",
            "right", "string-set",
            "text-align", "text-decoration", "text-decoration-color", "text-decoration-line", "text-decoration-style",
            "text-indent", "top",
            "transform", "transform-origin",
            "vertical-align", "visibility",
            "white-space", "width", "word-break", "word-spacing",
            "z-index",
            "align-content", "align-items", "align-self",
            "flex", "flex-basis", "flex-direction", "flex-flow", "flex-grow", "flex-shrink", "flex-wrap",
            "justify-content", "order",
            "gap", "row-gap", "column-gap",
        ];

        /// <summary>
        /// Snapshots all known property values from a CssBox into a dictionary.
        /// Used to capture the revert target between cascade origin phases.
        /// </summary>
        public static Dictionary<string, string?> SnapshotProperties(CssBox box)
        {
            var snapshot = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in _knownPropertyNames)
                snapshot[name] = GetPropertyValue(box, name);
            return snapshot;
        }

        /// <summary>
        /// Snapshots this box's custom property values, for use as the revert/revert-layer target of a
        /// later cascade phase. Custom property names are case-sensitive, unlike <see cref="SnapshotProperties"/>'s
        /// known-property snapshot, so this uses a separate, ordinal-case-sensitive dictionary.
        /// </summary>
        public static Dictionary<string, string>? SnapshotCustomProperties(CssBox box)
        {
            return box.CustomProperties is { Count: > 0 } ? new Dictionary<string, string>(box.CustomProperties) : null;
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
                case "border-radius":
                    cssBox.BorderRadius = value;
                    break;
                case "transform":
                    cssBox.Transform = value;
                    break;
                case "transform-origin":
                    cssBox.TransformOrigin = value;
                    break;
                case "border-top-left-radius":
                    cssBox.BorderTopLeftRadius = value;
                    break;
                case "border-top-right-radius":
                    cssBox.BorderTopRightRadius = value;
                    break;
                case "border-bottom-right-radius":
                    cssBox.BorderBottomRightRadius = value;
                    break;
                case "border-bottom-left-radius":
                    cssBox.BorderBottomLeftRadius = value;
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
                case "page":
                    cssBox.PageName = value;
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
                case "min-width":
                    if (IsValidLengthProperty(value) || value == "0")
                    {
                        cssBox.MinWidth = value;
                    }

                    break;
                case "height":
                    if (IsValidLengthProperty(value))
                    {
                        cssBox.Height = value;
                    }

                    break;
                case "max-height":
                    if (IsValidLengthProperty(value))
                    {
                        cssBox.MaxHeight = value;
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
                    cssBox.BackgroundImages = valueParser.ParseImages(value);
                    break;
                case "background-position":
                    cssBox.BackgroundPosition = value;
                    break;
                case "background-repeat":
                    cssBox.BackgroundRepeat = value;
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
                    SetListStyle(valueParser, cssBox, value);
                    break;
                case "list-style-position":
                    cssBox.ListStylePosition = value;
                    break;
                case "list-style-image":
                    cssBox.ListStyleImage = valueParser.ParseImage(value);
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
                case "background-origin":
                    cssBox.BackgroundOrigin = value;
                    break;
                case "background-clip":
                    cssBox.BackgroundClip = value;
                    break;
                case "flex-direction":
                    cssBox.FlexDirection = value;
                    break;
                case "flex-wrap":
                    cssBox.FlexWrap = value;
                    break;
                case "justify-content":
                    cssBox.JustifyContent = value;
                    break;
                case "align-items":
                    cssBox.AlignItems = value;
                    break;
                case "align-content":
                    cssBox.AlignContent = value;
                    break;
                case "flex-grow":
                    cssBox.FlexGrow = value;
                    break;
                case "flex-shrink":
                    cssBox.FlexShrink = value;
                    break;
                case "flex-basis":
                    cssBox.FlexBasis = value;
                    break;
                case "align-self":
                    cssBox.AlignSelf = value;
                    break;
                case "order":
                    cssBox.Order = value;
                    break;
                case "flex":
                    SetFlexShorthand(cssBox, value);
                    break;
                case "flex-flow":
                    SetFlexFlowShorthand(cssBox, value);
                    break;
                case "gap":
                    SetFlexGapShorthand(cssBox, value);
                    break;
                case "row-gap":
                    cssBox.FlexRowGap = value;
                    break;
                case "column-gap":
                    cssBox.FlexColumnGap = value;
                    break;
                case "unicode-bidi":
                case "background-attachment":
                case "overflow-wrap":
                    break;
            }
        }

        private static void SetFlexShorthand(CssBox cssBox, string value)
        {
            switch (value)
            {
                case "none": cssBox.FlexGrow = "0"; cssBox.FlexShrink = "0"; cssBox.FlexBasis = "auto"; return;
                case "auto": cssBox.FlexGrow = "1"; cssBox.FlexShrink = "1"; cssBox.FlexBasis = "auto"; return;
            }
            // TODO: a calc()-valued flex-basis component (e.g. "flex: 1 1 calc(10px + 5px)") would be
            // mis-split here, same as gap was - use CssValueParser.SplitTopLevelWhitespace if that's needed.
            var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 1 && float.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _))
                cssBox.FlexGrow = parts[0];
            if (parts.Length >= 2 && float.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _))
                cssBox.FlexShrink = parts[1];
            if (parts.Length >= 3)
                cssBox.FlexBasis = parts[2];
            else if (parts.Length == 1)
            {
                cssBox.FlexShrink = "1";
                cssBox.FlexBasis = "0";
            }
        }

        private static void SetFlexFlowShorthand(CssBox cssBox, string value)
        {
            foreach (var part in value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                switch (part)
                {
                    case "row":
                    case "row-reverse":
                    case "column":
                    case "column-reverse":
                        cssBox.FlexDirection = part;
                        break;
                    case "nowrap":
                    case "wrap":
                    case "wrap-reverse":
                        cssBox.FlexWrap = part;
                        break;
                }
            }
        }

        private static void SetFlexGapShorthand(CssBox cssBox, string value)
        {
            // Paren-depth-aware split so a calc()/min()/max()/clamp() value's internal spaces aren't
            // mistaken for the row/column delimiter (e.g. "gap: calc(10px + 5px) 20px").
            var parts = CssValueParser.SplitTopLevelWhitespace(value).ToArray();
            cssBox.FlexRowGap    = parts[0];
            cssBox.FlexColumnGap = parts.Length > 1 ? parts[1] : parts[0];
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

                if (value is not null && value.Equals(CssConstants.CurrentColor, StringComparison.OrdinalIgnoreCase))
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
            return propValue.Equals(CssConstants.CurrentColor, StringComparison.OrdinalIgnoreCase) ||
                   valueParser.IsColorValid(propValue);
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

        private static void SetListStyle(CssValueParser valueParser, CssBox box, string propValue)
        {
            var values = SplitValues(propValue);

            var listStyleType = CssConstants.None;
            CssImage? listStyleImage = null;
            var listStylePosition = CssConstants.Outside;

            foreach (var value in values)
            {
                if (value is CssConstants.Inside or CssConstants.Outside)
                {
                    listStylePosition = value;
                    continue;
                }

                var imageValue = valueParser.ParseImage(value);

                if (imageValue is null)
                    listStyleType = value;
                else
                    listStyleImage = imageValue;
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