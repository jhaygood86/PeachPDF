using PeachPDF.CSS;
using PeachPDF.Html.Core.Entities;
using PeachPDF.Html.Core.Utils;
using System;
using System.Collections.Generic;

namespace PeachPDF.Html.Core.Dom
{
    /// <summary>
    /// The set of CSS properties cascaded onto a <see cref="CssBox"/> from any source (author/UA
    /// stylesheets, inline <c>style=""</c>, translated presentational HTML attributes) - the "specified
    /// value" half of what <c>CssBoxProperties</c> used to hold. Values calculated FROM these (the
    /// <c>Actual*</c> family) live on <see cref="DerivedStyle"/> instead; layout-engine output
    /// (<c>Location</c>/<c>Size</c>/etc.) and non-cascaded box state (<c>UsedPageName</c>,
    /// <c>SubgridContext</c>) stay directly on <see cref="CssBox"/>.
    /// <para>
    /// Every property is <c>{ get; init; }</c> - there is no public way to mutate an existing instance.
    /// <see cref="Default"/> is therefore safe to share across every <see cref="CssBox"/> that hasn't had
    /// a property set yet; "changing" a value always means producing a new instance through
    /// <see cref="SetPropertyValue{T}"/>, the only place in the codebase allowed to use this record's
    /// <c>with</c> expression.
    /// </para>
    /// </summary>
    internal sealed record ComputedStyle
    {
        /// <summary>The CSS spec initial value for every property below - what a fresh, unstyled <see cref="CssBox"/> starts with.</summary>
        internal static readonly ComputedStyle Default = new();

        /// <summary>
        /// Produces the <see cref="ComputedStyle"/> that results from changing one property to
        /// <paramref name="newValue"/> - or, if it already holds that value, returns this same instance
        /// unchanged. This is the only sanctioned way to change a property: it is always safe to call
        /// regardless of whether this instance is the shared <see cref="Default"/> or already customized,
        /// and a no-op write allocates nothing.
        /// </summary>
        /// <param name="currentValue">The property's current value on this instance.</param>
        /// <param name="newValue">The value being assigned.</param>
        /// <param name="apply">Produces the updated record via a <c>with</c> expression, e.g. <c>static (s, v) =&gt; s with { Color = v }</c>.</param>
        internal ComputedStyle SetPropertyValue<T>(T currentValue, T newValue, Func<ComputedStyle, T, ComputedStyle> apply) =>
            EqualityComparer<T>.Default.Equals(currentValue, newValue) ? this : apply(this, newValue);

        #region Custom properties

        /// <summary>
        /// Specified (not var()-resolved) values of this box's CSS custom properties (--foo), keyed by
        /// their case-sensitive name. Null when no custom property has been declared or inherited.
        /// </summary>
        public Dictionary<string, string>? CustomProperties { get; init; }

        #endregion

        #region Border widths, styles, colors, radii

        public string BorderTopWidth { get; init; } = "medium";
        public string BorderRightWidth { get; init; } = "medium";
        public string BorderBottomWidth { get; init; } = "medium";
        public string BorderLeftWidth { get; init; } = "medium";

        public string BorderTopStyle { get; init; } = "none";
        public string BorderRightStyle { get; init; } = "none";
        public string BorderBottomStyle { get; init; } = "none";
        public string BorderLeftStyle { get; init; } = "none";

        public string BorderTopColor { get; init; } = "black";
        public string BorderRightColor { get; init; } = "black";
        public string BorderBottomColor { get; init; } = "black";
        public string BorderLeftColor { get; init; } = "black";

        public string BorderTopLeftRadius { get; init; } = "0";
        public string BorderTopRightRadius { get; init; } = "0";
        public string BorderBottomRightRadius { get; init; } = "0";
        public string BorderBottomLeftRadius { get; init; } = "0";

        public string BorderSpacing { get; init; } = "0";
        public string BorderCollapse { get; init; } = "separate";

        #endregion

        #region Transform, opacity, clip-path, aspect-ratio, box-shadow

        public string Transform { get; init; } = CssConstants.None;
        public string TransformOrigin { get; init; } = "50% 50% 0";
        public string Opacity { get; init; } = "1";
        public string ClipPath { get; init; } = CssConstants.None;
        public string AspectRatio { get; init; } = CssConstants.Auto;
        public string BoxShadow { get; init; } = CssConstants.None;
        public string BoxSizing { get; init; } = CssConstants.ContentBox;

        #endregion

        #region Counters, page, pdf-tag-type

        public string CounterIncrement { get; init; } = CssConstants.None;
        public string CounterReset { get; init; } = CssConstants.None;
        public string CounterSet { get; init; } = CssConstants.None;
        public string StringSet { get; init; } = CssConstants.None;
        public string PageName { get; init; } = string.Empty;
        public string PdfTagType { get; init; } = CssConstants.Auto;

        #endregion

        #region Margin, padding

        public string MarginTop { get; init; } = "0";
        public string MarginRight { get; init; } = "0";
        public string MarginBottom { get; init; } = "0";
        public string MarginLeft { get; init; } = "0";

        public string PaddingTop { get; init; } = "0";
        public string PaddingRight { get; init; } = "0";
        public string PaddingBottom { get; init; } = "0";
        public string PaddingLeft { get; init; } = "0";

        #endregion

        #region Break, box-decoration-break, orphans/widows

        public string BreakBefore { get; init; } = CssConstants.Auto;
        public string BreakInside { get; init; } = CssConstants.Auto;
        public string BreakAfter { get; init; } = CssConstants.Auto;
        public string BoxDecorationBreak { get; init; } = CssConstants.Slice;
        public string Orphans { get; init; } = "2";
        public string Widows { get; init; } = "2";

        #endregion

        #region Box position and size

        public string Left { get; init; } = CssConstants.Auto;
        public string Top { get; init; } = CssConstants.Auto;
        public string Bottom { get; init; } = CssConstants.Auto;
        public string Right { get; init; } = CssConstants.Auto;

        public string Width { get; init; } = "auto";
        public string MaxWidth { get; init; } = "none";
        public string MinWidth { get; init; } = "0";

        public string Height { get; init; } = "auto";
        public string MaxHeight { get; init; } = "none";
        public string MinHeight { get; init; } = "auto";

        #endregion

        #region Background

        public string BackgroundColor { get; init; } = "transparent";
        public IReadOnlyList<CssImage>? BackgroundImages { get; init; }
        public string BackgroundPosition { get; init; } = "0% 0%";
        public string BackgroundRepeat { get; init; } = "repeat";
        public string BackgroundSize { get; init; } = CssConstants.Auto;
        public string BackgroundOrigin { get; init; } = CssConstants.PaddingBox;
        public string BackgroundClip { get; init; } = CssConstants.BorderBox;
        public string BackgroundAttachment { get; init; } = CssConstants.Scroll;
        public string ObjectFit { get; init; } = CssConstants.Fill;
        public string ObjectPosition { get; init; } = "50% 50%";

        #endregion

        #region Color, content, display, direction, float, clear, position

        public string Color { get; init; } = "black";
        public string Content { get; init; } = CssConstants.Normal;
        public string Display { get; init; } = "inline";
        public string Direction { get; init; } = "ltr";
        public string EmptyCells { get; init; } = "show";
        public string Float { get; init; } = CssConstants.None;
        public string Clear { get; init; } = CssConstants.None;
        public string Position { get; init; } = "static";

        #endregion

        #region Line-height, vertical-align, text-*

        public string LineHeight { get; init; } = "normal";
        public string VerticalAlign { get; init; } = "baseline";
        public string TextIndent { get; init; } = "0";
        public string TextAlign { get; init; } = string.Empty;
        public string TextDecorationLine { get; init; } = string.Empty;
        public string TextDecorationStyle { get; init; } = string.Empty;
        public string TextDecorationColor { get; init; } = string.Empty;
        public string TextTransform { get; init; } = CssConstants.None;
        public string WhiteSpace { get; init; } = "normal";
        public string Visibility { get; init; } = "visible";
        public string WordSpacing { get; init; } = "normal";
        public string LetterSpacing { get; init; } = "normal";
        public string WordBreak { get; init; } = "normal";

        /// <summary>
        /// <c>none</c>/<c>manual</c>/<c>auto</c>. <c>manual</c> and <c>auto</c> both honor an explicit
        /// soft hyphen (U+00AD) as a line-break opportunity; only <c>auto</c> additionally implies
        /// dictionary-based automatic hyphenation, which PeachPDF does not implement (see
        /// docs/html-css-support.md) - so in practice <c>auto</c> behaves like <c>manual</c> here.
        /// </summary>
        public string Hyphens { get; init; } = "manual";

        #endregion

        #region Font

        public string? FontFamily { get; init; }

        /// <summary>
        /// The full, unresolved <c>font-family</c> list as authored (e.g. <c>"Latin", "Symbols", serif</c>),
        /// as opposed to <see cref="FontFamily"/> which the cascade already collapses to the first family
        /// that exists. Retained so per-codepoint font matching can walk the whole stack. Inherited alongside <see cref="FontFamily"/>.
        /// </summary>
        public string? FontFamilyList { get; init; }

        public string FontSize { get; init; } = "medium";
        public string FontStyle { get; init; } = "normal";
        public string FontVariant { get; init; } = "normal";
        public string FontWeight { get; init; } = "normal";
        public string FontStretch { get; init; } = "normal";
        public string FontPalette { get; init; } = "normal";

        #endregion

        #region Overflow, list-style

        public string Overflow { get; init; } = "visible";
        public string ListStylePosition { get; init; } = "outside";
        public CssImage? ListStyleImage { get; init; }
        public string ListStyleType { get; init; } = "disc";

        #endregion

        #region Z-index

        public string ZIndex { get; init; } = CssConstants.Auto;

        #endregion

        #region Flex container/item

        public string FlexDirection { get; init; } = "row";
        public string FlexWrap { get; init; } = "nowrap";
        public string JustifyContent { get; init; } = "normal";
        public string AlignItems { get; init; } = "normal";
        public string AlignContent { get; init; } = "normal";

        public string FlexGrow { get; init; } = "0";
        public string FlexShrink { get; init; } = "1";
        public string FlexBasis { get; init; } = "auto";
        public string AlignSelf { get; init; } = "auto";
        public string Order { get; init; } = "0";

        /// <summary>Gap between flex items - main-axis gap for row direction.</summary>
        public string FlexRowGap { get; init; } = "0";
        public string FlexColumnGap { get; init; } = "0";

        #endregion

        #region Grid container/item

        // grid-template-columns/rows carry the parsed GridTemplate through the cascade (CssProperty<T>) so
        // the layout engine reads the already-parsed value; the initial value is `none` (a null GridTemplate).
        public CssProperty<GridTemplate> GridTemplateColumns { get; init; } = CssProperty<GridTemplate>.FromValue(CssConstants.None, null);
        public CssProperty<GridTemplate> GridTemplateRows { get; init; } = CssProperty<GridTemplate>.FromValue(CssConstants.None, null);
        public string GridTemplateAreas { get; init; } = CssConstants.None;
        public string GridAutoColumns { get; init; } = CssConstants.Auto;
        public string GridAutoRows { get; init; } = CssConstants.Auto;
        public string GridAutoFlow { get; init; } = CssConstants.Row;
        public string JustifyItems { get; init; } = CssConstants.Normal;
        public string JustifySelf { get; init; } = CssConstants.Auto;

        public string GridColumnStart { get; init; } = CssConstants.Auto;
        public string GridColumnEnd { get; init; } = CssConstants.Auto;
        public string GridRowStart { get; init; } = CssConstants.Auto;
        public string GridRowEnd { get; init; } = CssConstants.Auto;

        #endregion

        #region Multi-column

        public string ColumnCount { get; init; } = CssConstants.Auto;
        public string ColumnWidth { get; init; } = CssConstants.Auto;
        public string ColumnFill { get; init; } = "balance";
        public string ColumnSpan { get; init; } = CssConstants.None;
        public string ColumnRuleWidth { get; init; } = CssConstants.Medium;
        public string ColumnRuleStyle { get; init; } = CssConstants.None;
        public string ColumnRuleColor { get; init; } = CssConstants.CurrentColor;

        #endregion
    }
}
