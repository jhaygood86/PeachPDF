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

using PeachPDF.CSS;
using PeachPDF.Html.Adapters;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Entities;
using PeachPDF.Html.Core.Parse;
using System;
using System.Collections.Frozen;
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
                // word-spacing is a plain length in the same layout coordinate space as margin/padding/
                // width/etc.; ParseLength resolves every unit (including spec-correct CSS px) through
                // the shared Length.ToPixels conversion.
                w += CssValueParser.ParseLength(box.WordSpacing, 0, box);
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
        public static string? GetPropertyValue(CssBox cssBox, string propName) =>
            _propertyGetters.TryGetValue(propName, out var getter) ? getter(cssBox) : null;

        /// <summary>
        /// Maps a CSS property name to the getter for its stored <see cref="CssBox"/> value. An unlisted
        /// name resolves to <c>null</c> (the switch's old <c>_ =&gt; null</c> default). <c>background-image</c>
        /// and <c>list-style-image</c> are intentionally absent: they are parsed into structured fields, not
        /// retrievable as a raw string, so they returned <c>null</c> here before too.
        /// </summary>
        private static readonly FrozenDictionary<string, Func<CssBox, string?>> _propertyGetters =
            new Dictionary<string, Func<CssBox, string?>>
            {
                ["border-bottom-width"] = b => b.BorderBottomWidth,
                ["border-left-width"] = b => b.BorderLeftWidth,
                ["border-right-width"] = b => b.BorderRightWidth,
                ["border-top-width"] = b => b.BorderTopWidth,
                ["border-bottom-style"] = b => b.BorderBottomStyle,
                ["border-left-style"] = b => b.BorderLeftStyle,
                ["border-right-style"] = b => b.BorderRightStyle,
                ["border-top-style"] = b => b.BorderTopStyle,
                ["border-bottom-color"] = b => b.BorderBottomColor,
                ["border-left-color"] = b => b.BorderLeftColor,
                ["border-right-color"] = b => b.BorderRightColor,
                ["border-top-color"] = b => b.BorderTopColor,
                ["border-spacing"] = b => b.BorderSpacing,
                ["border-collapse"] = b => b.BorderCollapse,
                ["box-sizing"] = b => b.BoxSizing,
                ["break-after"] = b => b.BreakAfter,
                ["break-before"] = b => b.BreakBefore,
                ["break-inside"] = b => b.BreakInside,
                ["orphans"] = b => b.Orphans,
                ["widows"] = b => b.Widows,
                ["hyphens"] = b => b.Hyphens,
                ["border-top-left-radius"] = b => b.BorderTopLeftRadius,
                ["border-top-right-radius"] = b => b.BorderTopRightRadius,
                ["border-bottom-right-radius"] = b => b.BorderBottomRightRadius,
                ["border-bottom-left-radius"] = b => b.BorderBottomLeftRadius,
                ["counter-increment"] = b => b.CounterIncrement,
                ["counter-reset"] = b => b.CounterReset,
                ["counter-set"] = b => b.CounterSet,
                ["string-set"] = b => b.StringSet,
                ["page"] = b => b.PageName,
                ["-peachpdf-pdf-tag-type"] = b => b.PdfTagType,
                ["margin-bottom"] = b => b.MarginBottom,
                ["margin-left"] = b => b.MarginLeft,
                ["margin-right"] = b => b.MarginRight,
                ["margin-top"] = b => b.MarginTop,
                ["padding-bottom"] = b => b.PaddingBottom,
                ["padding-left"] = b => b.PaddingLeft,
                ["padding-right"] = b => b.PaddingRight,
                ["padding-top"] = b => b.PaddingTop,
                ["page-break-after"] = b => b.BreakAfter,
                ["page-break-before"] = b => b.BreakBefore,
                ["page-break-inside"] = b => b.BreakInside,
                ["left"] = b => b.Left,
                ["top"] = b => b.Top,
                ["right"] = b => b.Right,
                ["bottom"] = b => b.Bottom,
                ["width"] = b => b.Width,
                ["max-width"] = b => b.MaxWidth,
                ["min-width"] = b => b.MinWidth,
                ["height"] = b => b.Height,
                ["max-height"] = b => b.MaxHeight,
                ["min-height"] = b => b.MinHeight,
                ["transform"] = b => b.Transform,
                ["transform-origin"] = b => b.TransformOrigin,
                ["clip-path"] = b => b.ClipPath,
                ["aspect-ratio"] = b => b.AspectRatio,
                ["box-shadow"] = b => b.BoxShadow,
                ["opacity"] = b => b.Opacity,
                ["background-color"] = b => b.BackgroundColor,
                ["background-position"] = b => b.BackgroundPosition,
                ["background-size"] = b => b.BackgroundSize,
                ["background-repeat"] = b => b.BackgroundRepeat,
                ["background-origin"] = b => b.BackgroundOrigin,
                ["background-clip"] = b => b.BackgroundClip,
                ["background-attachment"] = b => b.BackgroundAttachment,
                ["object-fit"] = b => b.ObjectFit,
                ["object-position"] = b => b.ObjectPosition,
                ["content"] = b => b.Content,
                ["color"] = b => b.Color,
                ["display"] = b => b.Display,
                ["direction"] = b => b.Direction,
                ["empty-cells"] = b => b.EmptyCells,
                ["float"] = b => b.Float,
                ["clear"] = b => b.Clear,
                ["position"] = b => b.Position,
                ["line-height"] = b => b.LineHeight,
                ["vertical-align"] = b => b.VerticalAlign,
                ["text-indent"] = b => b.TextIndent,
                ["text-align"] = b => b.TextAlign,
                ["text-decoration-color"] = b => b.TextDecorationColor,
                ["text-decoration-line"] = b => b.TextDecorationLine,
                ["text-decoration-style"] = b => b.TextDecorationStyle,
                ["text-transform"] = b => b.TextTransform,
                ["white-space"] = b => b.WhiteSpace,
                ["word-break"] = b => b.WordBreak,
                ["visibility"] = b => b.Visibility,
                ["word-spacing"] = b => b.WordSpacing,
                ["letter-spacing"] = b => b.LetterSpacing,
                ["font-family"] = b => b.FontFamily,
                ["font-size"] = b => b.FontSize,
                ["font-style"] = b => b.FontStyle,
                ["font-variant"] = b => b.FontVariant,
                ["font-weight"] = b => b.FontWeight,
                ["font-stretch"] = b => b.FontStretch,
                ["font-palette"] = b => b.FontPalette,
                ["list-style-position"] = b => b.ListStylePosition,
                ["list-style-type"] = b => b.ListStyleType,
                ["overflow"] = b => b.Overflow,
                ["z-index"] = b => b.ZIndex,
                ["flex-direction"] = b => b.FlexDirection,
                ["flex-wrap"] = b => b.FlexWrap,
                ["justify-content"] = b => b.JustifyContent,
                ["align-items"] = b => b.AlignItems,
                ["align-content"] = b => b.AlignContent,
                ["flex-grow"] = b => b.FlexGrow,
                ["flex-shrink"] = b => b.FlexShrink,
                ["flex-basis"] = b => b.FlexBasis,
                ["align-self"] = b => b.AlignSelf,
                ["order"] = b => b.Order,
                ["row-gap"] = b => b.FlexRowGap,
                ["column-gap"] = b => b.FlexColumnGap,
                ["grid-template-columns"] = b => b.GridTemplateColumns.ToString(),
                ["grid-template-rows"] = b => b.GridTemplateRows.ToString(),
                ["grid-template-areas"] = b => b.GridTemplateAreas,
                ["grid-auto-columns"] = b => b.GridAutoColumns,
                ["grid-auto-rows"] = b => b.GridAutoRows,
                ["grid-auto-flow"] = b => b.GridAutoFlow,
                ["justify-items"] = b => b.JustifyItems,
                ["justify-self"] = b => b.JustifySelf,
                ["grid-column-start"] = b => b.GridColumnStart,
                ["grid-column-end"] = b => b.GridColumnEnd,
                ["grid-row-start"] = b => b.GridRowStart,
                ["grid-row-end"] = b => b.GridRowEnd,
                ["column-count"] = b => b.ColumnCount,
                ["column-width"] = b => b.ColumnWidth,
                ["column-fill"] = b => b.ColumnFill,
                ["column-span"] = b => b.ColumnSpan,
                ["column-rule-width"] = b => b.ColumnRuleWidth,
                ["column-rule-style"] = b => b.ColumnRuleStyle,
                ["column-rule-color"] = b => b.ColumnRuleColor,
            }.ToFrozenDictionary(StringComparer.Ordinal);

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
            "break-after", "break-before", "break-inside", "orphans", "widows", "hyphens",
            "clear", "color", "content",
            "counter-increment", "counter-reset", "counter-set",
            "direction", "display",
            "empty-cells", "float",
            "font-family", "font-size", "font-stretch", "font-style", "font-variant", "font-weight", "font-palette",
            "height",
            "left", "letter-spacing", "line-height",
            "list-style-image", "list-style-position", "list-style-type",
            "margin-bottom", "margin-left", "margin-right", "margin-top",
            "max-width", "min-width", "max-height", "min-height",
            "object-fit", "object-position",
            "overflow",
            "padding-bottom", "padding-left", "padding-right", "padding-top",
            "position",
            "right", "string-set",
            "text-align", "text-decoration-color", "text-decoration-line", "text-decoration-style",
            "text-indent", "text-transform", "top",
            "transform", "transform-origin", "clip-path", "aspect-ratio", "box-shadow", "opacity",
            "vertical-align", "visibility",
            "white-space", "width", "word-break", "word-spacing",
            "z-index",
            "align-content", "align-items", "align-self",
            "flex-basis", "flex-direction", "flex-grow", "flex-shrink", "flex-wrap",
            "justify-content", "order",
            "row-gap", "column-gap",
            "grid-template-columns", "grid-template-rows", "grid-template-areas",
            "grid-auto-columns", "grid-auto-rows",
            "grid-auto-flow", "justify-items", "justify-self",
            "grid-column-start", "grid-column-end", "grid-row-start", "grid-row-end",
            "column-count", "column-width", "column-fill", "column-span",
            "column-rule-width", "column-rule-style", "column-rule-color",
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
            if (_propertySetters.TryGetValue(propName, out var setter))
            {
                setter(valueParser, cssBox, value);
            }
        }

        /// <summary>
        /// Assigns a property's already-parsed, strongly-typed value straight onto a <see cref="CssBox"/> from
        /// its Layer A <see cref="ITypedPropertyValue{T}"/> carrier, without re-parsing the authored string.
        /// Returns false when <paramref name="propName"/> has no typed setter, or when
        /// <paramref name="declaredValue"/> is not the matching typed carrier (e.g. a global-keyword value) — the
        /// caller then falls back to the string setter. The per-name handler knows the concrete <c>T</c>.
        /// </summary>
        public static bool TrySetTypedPropertyValue(CssBox cssBox, string propName, IPropertyValue declaredValue)
        {
            return declaredValue is not null
                   && _typedPropertySetters.TryGetValue(propName, out var setter)
                   && setter(cssBox, declaredValue);
        }

        private static readonly FrozenDictionary<string, Func<CssBox, IPropertyValue, bool>> _typedPropertySetters =
            new Dictionary<string, Func<CssBox, IPropertyValue, bool>>
            {
                ["grid-template-columns"] = (b, dv) =>
                {
                    if (!dv.TryGetValue<GridTemplate>(out var t)) return false;
                    b.GridTemplateColumns = t;
                    return true;
                },
                ["grid-template-rows"] = (b, dv) =>
                {
                    if (!dv.TryGetValue<GridTemplate>(out var t)) return false;
                    b.GridTemplateRows = t;
                    return true;
                },
            }.ToFrozenDictionary(StringComparer.Ordinal);


        /// <summary>
        /// Maps a CSS property name to the action that assigns its parsed value onto a <see cref="CssBox"/>,
        /// preserving each property's validation guard and shorthand expansion. An unlisted name is a no-op
        /// (the switch's old empty default). Replaces a 134-case switch with a single dictionary lookup so the
        /// dispatch method's cyclomatic complexity — and its CRAP score — stays flat as properties are added.
        /// </summary>
        private static readonly FrozenDictionary<string, Action<CssValueParser, CssBox, string>> _propertySetters =
            new Dictionary<string, Action<CssValueParser, CssBox, string>>
            {
                ["border-bottom-width"] = (_, b, v) => { if (IsValidLengthProperty(v)) b.BorderBottomWidth = v; },
                ["border-left-width"] = (_, b, v) => { if (IsValidLengthProperty(v)) b.BorderLeftWidth = v; },
                ["border-right-width"] = (_, b, v) => { if (IsValidLengthProperty(v)) b.BorderRightWidth = v; },
                ["border-top-width"] = (_, b, v) => { if (IsValidLengthProperty(v)) b.BorderTopWidth = v; },
                ["border-bottom-style"] = (_, b, v) => { if (IsValidBorderStyleProperty(v)) b.BorderBottomStyle = v; },
                ["border-left-style"] = (_, b, v) => { if (IsValidBorderStyleProperty(v)) b.BorderLeftStyle = v; },
                ["border-right-style"] = (_, b, v) => { if (IsValidBorderStyleProperty(v)) b.BorderRightStyle = v; },
                ["border-top-style"] = (_, b, v) => { if (IsValidBorderStyleProperty(v)) b.BorderTopStyle = v; },
                ["border-bottom-color"] = (p, b, v) => { if (IsValidColorProperty(p, v)) b.BorderBottomColor = v; },
                ["border-left-color"] = (p, b, v) => { if (IsValidColorProperty(p, v)) b.BorderLeftColor = v; },
                ["border-right-color"] = (p, b, v) => { if (IsValidColorProperty(p, v)) b.BorderRightColor = v; },
                ["border-top-color"] = (p, b, v) => { if (IsValidColorProperty(p, v)) b.BorderTopColor = v; },
                ["border-spacing"] = (_, b, v) => b.BorderSpacing = v,
                ["border-collapse"] = (_, b, v) => b.BorderCollapse = v,
                ["box-sizing"] = (_, b, v) => { if (IsValidBoxSizing(v)) b.BoxSizing = v; },
                ["transform"] = (_, b, v) => b.Transform = v,
                ["transform-origin"] = (_, b, v) => b.TransformOrigin = v,
                ["clip-path"] = (_, b, v) => b.ClipPath = v,
                ["aspect-ratio"] = (_, b, v) => b.AspectRatio = v,
                ["box-shadow"] = (_, b, v) => b.BoxShadow = v,
                ["opacity"] = (_, b, v) => b.Opacity = v,
                ["border-top-left-radius"] = (_, b, v) => b.BorderTopLeftRadius = v,
                ["border-top-right-radius"] = (_, b, v) => b.BorderTopRightRadius = v,
                ["border-bottom-right-radius"] = (_, b, v) => b.BorderBottomRightRadius = v,
                ["border-bottom-left-radius"] = (_, b, v) => b.BorderBottomLeftRadius = v,
                ["counter-increment"] = (_, b, v) => b.CounterIncrement = v,
                ["counter-reset"] = (_, b, v) => b.CounterReset = v,
                ["counter-set"] = (_, b, v) => b.CounterSet = v,
                ["string-set"] = (_, b, v) => b.StringSet = v,
                ["page"] = (_, b, v) => b.PageName = v,
                ["-peachpdf-pdf-tag-type"] = (_, b, v) => b.PdfTagType = v,
                ["margin-bottom"] = (_, b, v) => b.MarginBottom = v,
                ["margin-left"] = (_, b, v) => b.MarginLeft = v,
                ["margin-right"] = (_, b, v) => b.MarginRight = v,
                ["margin-top"] = (_, b, v) => b.MarginTop = v,
                ["padding-bottom"] = (_, b, v) => b.PaddingBottom = v,
                ["padding-left"] = (_, b, v) => b.PaddingLeft = v,
                ["padding-right"] = (_, b, v) => b.PaddingRight = v,
                ["padding-top"] = (_, b, v) => b.PaddingTop = v,
                // page-break-* is the legacy alias of break-*; on the -after/-before axis "always" maps to "page".
                ["break-after"] = (_, b, v) => b.BreakAfter = v,
                ["page-break-after"] = (_, b, v) => b.BreakAfter = v is CssConstants.Always ? CssConstants.Page : v,
                ["break-before"] = (_, b, v) => b.BreakBefore = v,
                ["page-break-before"] = (_, b, v) => b.BreakBefore = v is CssConstants.Always ? CssConstants.Page : v,
                ["break-inside"] = (_, b, v) => b.BreakInside = v,
                ["page-break-inside"] = (_, b, v) => b.BreakInside = v,
                ["orphans"] = (_, b, v) => { if (int.TryParse(v, out var orphans) && orphans >= 1) b.Orphans = v; },
                ["widows"] = (_, b, v) => { if (int.TryParse(v, out var widows) && widows >= 1) b.Widows = v; },
                ["hyphens"] = (_, b, v) => { if (v is CssConstants.None or "manual" or CssConstants.Auto) b.Hyphens = v; },
                ["left"] = (_, b, v) => b.Left = v,
                ["top"] = (_, b, v) => b.Top = v,
                ["right"] = (_, b, v) => b.Right = v,
                ["bottom"] = (_, b, v) => b.Bottom = v,
                ["width"] = (_, b, v) => { if (IsValidLengthProperty(v)) b.Width = v; },
                ["max-width"] = (_, b, v) => { if (IsValidLengthProperty(v)) b.MaxWidth = v; },
                ["min-width"] = (_, b, v) => { if (IsValidLengthProperty(v)) b.MinWidth = v; },
                ["height"] = (_, b, v) => { if (IsValidLengthProperty(v)) b.Height = v; },
                ["max-height"] = (_, b, v) => { if (IsValidLengthProperty(v)) b.MaxHeight = v; },
                ["min-height"] = (_, b, v) => { if (IsValidLengthProperty(v)) b.MinHeight = v; },
                ["background-color"] = (p, b, v) => { if (IsValidColorProperty(p, v)) b.BackgroundColor = v; },
                ["background-image"] = (p, b, v) => b.BackgroundImages = p.ParseImages(v),
                ["background-position"] = (_, b, v) => b.BackgroundPosition = v,
                ["background-size"] = (_, b, v) => b.BackgroundSize = v,
                ["background-repeat"] = (_, b, v) => b.BackgroundRepeat = v,
                ["color"] = (p, b, v) => { if (IsValidColorProperty(p, v)) b.Color = v; },
                ["content"] = (_, b, v) => b.Content = v,
                ["display"] = (_, b, v) => b.Display = v,
                ["direction"] = (_, b, v) => b.Direction = v,
                ["empty-cells"] = (_, b, v) => b.EmptyCells = v,
                ["float"] = (_, b, v) => b.Float = v,
                ["clear"] = (_, b, v) => b.Clear = v,
                ["position"] = (_, b, v) => b.Position = v,
                // line-height's grammar (length | percentage | <number> | normal) is wider than
                // IsValidLengthProperty's, so a bare unitless multiplier like "1.4" must also be accepted.
                ["line-height"] = (p, b, v) =>
                {
                    if (IsValidLengthProperty(v) || v == CssConstants.Normal ||
                        double.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _))
                        b.LineHeight = v;
                },
                ["vertical-align"] = (_, b, v) => b.VerticalAlign = v,
                ["text-indent"] = (_, b, v) => b.TextIndent = v,
                ["text-align"] = (_, b, v) => b.TextAlign = v,
                ["text-decoration-color"] = (_, b, v) => b.TextDecorationColor = v,
                ["text-decoration-line"] = (_, b, v) => b.TextDecorationLine = v,
                ["text-decoration-style"] = (_, b, v) => b.TextDecorationStyle = v,
                ["text-transform"] = (_, b, v) => b.TextTransform = v,
                ["white-space"] = (_, b, v) => b.WhiteSpace = v,
                ["word-break"] = (_, b, v) => b.WordBreak = v,
                ["visibility"] = (_, b, v) => b.Visibility = v,
                ["word-spacing"] = (_, b, v) => b.WordSpacing = v,
                ["letter-spacing"] = (_, b, v) => b.LetterSpacing = v,
                ["font-family"] = (p, b, v) => { b.FontFamily = p.GetFontFamilyByName(v); b.FontFamilyList = v; },
                ["font-size"] = (_, b, v) => b.FontSize = v,
                ["font-style"] = (_, b, v) => b.FontStyle = v,
                ["font-variant"] = (_, b, v) => b.FontVariant = v,
                ["font-weight"] = (_, b, v) => b.FontWeight = v,
                ["font-stretch"] = (_, b, v) => b.FontStretch = v,
                ["font-palette"] = (_, b, v) => b.FontPalette = v,
                ["list-style-position"] = (_, b, v) => b.ListStylePosition = v,
                ["list-style-image"] = (p, b, v) => b.ListStyleImage = p.ParseImage(v),
                ["list-style-type"] = (_, b, v) => b.ListStyleType = v,
                ["overflow"] = (_, b, v) => b.Overflow = v,
                ["z-index"] = (p, b, v) => { if (v is CssConstants.Auto || int.TryParse(v, out _)) b.ZIndex = v; },
                ["background-origin"] = (_, b, v) => b.BackgroundOrigin = v,
                ["background-clip"] = (_, b, v) => b.BackgroundClip = v,
                ["background-attachment"] = (_, b, v) => b.BackgroundAttachment = v,
                ["object-fit"] = (_, b, v) => b.ObjectFit = v,
                ["object-position"] = (_, b, v) => b.ObjectPosition = v,
                ["flex-direction"] = (_, b, v) => b.FlexDirection = v,
                ["flex-wrap"] = (_, b, v) => b.FlexWrap = v,
                ["justify-content"] = (_, b, v) => b.JustifyContent = v,
                ["align-items"] = (_, b, v) => b.AlignItems = v,
                ["align-content"] = (_, b, v) => b.AlignContent = v,
                ["flex-grow"] = (_, b, v) => b.FlexGrow = v,
                ["flex-shrink"] = (_, b, v) => b.FlexShrink = v,
                ["flex-basis"] = (_, b, v) => b.FlexBasis = v,
                ["align-self"] = (_, b, v) => b.AlignSelf = v,
                ["order"] = (_, b, v) => b.Order = v,
                ["row-gap"] = (_, b, v) => b.FlexRowGap = v,
                ["column-gap"] = (_, b, v) => b.FlexColumnGap = v,
                ["grid-template-columns"] = (_, b, v) => b.GridTemplateColumns = GridTemplateValueConverter.FromCssText(v),
                ["grid-template-rows"] = (_, b, v) => b.GridTemplateRows = GridTemplateValueConverter.FromCssText(v),
                ["grid-template-areas"] = (_, b, v) => b.GridTemplateAreas = v,
                ["grid-auto-columns"] = (_, b, v) => b.GridAutoColumns = v,
                ["grid-auto-rows"] = (_, b, v) => b.GridAutoRows = v,
                ["grid-auto-flow"] = (_, b, v) => b.GridAutoFlow = v,
                ["justify-items"] = (_, b, v) => b.JustifyItems = v,
                ["justify-self"] = (_, b, v) => b.JustifySelf = v,
                ["grid-column-start"] = (_, b, v) => b.GridColumnStart = v,
                ["grid-column-end"] = (_, b, v) => b.GridColumnEnd = v,
                ["grid-row-start"] = (_, b, v) => b.GridRowStart = v,
                ["grid-row-end"] = (_, b, v) => b.GridRowEnd = v,
                ["column-count"] = (_, b, v) => { if (v is CssConstants.Auto || (int.TryParse(v, out var columnCount) && columnCount > 0)) b.ColumnCount = v; },
                ["column-width"] = (_, b, v) => { if (v is CssConstants.Auto || CssValueParser.IsValidLength(v)) b.ColumnWidth = v; },
                ["column-fill"] = (_, b, v) => { if (v is CssConstants.Auto or "balance") b.ColumnFill = v; },
                ["column-span"] = (_, b, v) => { if (v is CssConstants.None or "all") b.ColumnSpan = v; },
                ["column-rule-width"] = (_, b, v) => { if (IsValidLengthProperty(v)) b.ColumnRuleWidth = v; },
                ["column-rule-style"] = (_, b, v) => { if (IsValidBorderStyleProperty(v)) b.ColumnRuleStyle = v; },
                ["column-rule-color"] = (p, b, v) => { if (IsValidColorProperty(p, v)) b.ColumnRuleColor = v; },
                // Parsed and accepted but intentionally not applied (no layout effect yet).
                ["unicode-bidi"] = (_, _, _) => { },
                ["overflow-wrap"] = (_, _, _) => { },
            }.ToFrozenDictionary(StringComparer.Ordinal);

        // <'column-width'> || <'column-count'> — each token is either "auto" (leave that longhand at
        // its default), an integer (column-count), or a length (column-width).

        public static void ApplyCurrentColor(CssBox box, CssValueParser valueParser)
        {
            string[] colorProperties =
            [
                "border-top-color",
                "border-bottom-color",
                "border-left-color",
                "border-right-color",
                "background-color",
                "column-rule-color"
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

    }
}
