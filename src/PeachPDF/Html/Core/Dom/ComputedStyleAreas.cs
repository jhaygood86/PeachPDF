using PeachPDF.CSS;
using PeachPDF.Html.Core.Entities;
using System;
using System.Collections.Generic;
// The Text area below declares a property literally named WritingMode, which shadows the CSS-OM enum
// type of the same name within this file - alias it so the enum stays referenceable.
using WritingModeEnum = PeachPDF.CSS.WritingMode;
// Same shadowing issue for the DisplayPositioning area's ContainerType property below.
using ContainerTypeEnum = PeachPDF.CSS.ContainerType;

namespace PeachPDF.Html.Core.Dom
{
    /// <summary>
    /// The generic copy-on-write mechanisms shared by <see cref="ComputedStyle"/> and every area record
    /// below. Declared once here rather than as instance methods on 17 separate record types
    /// (<see cref="ComputedStyle"/> plus the 16 areas).
    /// </summary>
    internal static class ComputedStyleCow
    {
        /// <summary>
        /// Produces the record that results from changing one property to <paramref name="newValue"/> -
        /// or, if it already holds that value, returns <paramref name="self"/> unchanged. Safe to call
        /// unconditionally regardless of whether <paramref name="self"/> is a shared <c>Default</c>
        /// singleton or already customized, and a no-op write allocates nothing. Used at the "leaf"
        /// level, where <paramref name="currentValue"/>/<paramref name="newValue"/> are the actual CSS
        /// property value (a string, a <see cref="CssImage"/>, etc.) - see <see cref="AdoptArea{TSelf,TArea}"/>
        /// for the analogous operation one level up, where the value being compared is a whole area record.
        /// </summary>
        internal static TSelf SetPropertyValue<TSelf, TValue>(this TSelf self, TValue currentValue, TValue newValue, Func<TSelf, TValue, TSelf> apply) =>
            EqualityComparer<TValue>.Default.Equals(currentValue, newValue) ? self : apply(self, newValue);

        /// <summary>
        /// The area-level counterpart to <see cref="SetPropertyValue{TSelf,TValue}"/>: swaps
        /// <paramref name="newArea"/> onto <paramref name="self"/> (a <see cref="ComputedStyle"/>) unless
        /// it's already there. Compares by <em>reference</em>, not by structural (record) equality -
        /// deliberately, for two reasons. First, every call site passes a <paramref name="newArea"/> that
        /// is either exactly <paramref name="currentArea"/> (when the leaf-level <see cref="SetPropertyValue{TSelf,TValue}"/>
        /// call that produced it was itself a no-op) or a freshly-forked, therefore-necessarily-different
        /// object (when it changed a field) - so a structural comparison here would always agree with a
        /// reference comparison anyway, at the cost of scanning every field of the area for nothing.
        /// Second, in <see cref="CssBox.InheritStyle"/>'s whole-area adoption, <paramref name="currentArea"/>
        /// and <paramref name="newArea"/> come from two unrelated boxes - using reference equality there
        /// means two boxes whose areas happen to be equal in content but are separate objects still get
        /// unified onto the same shared instance, which is what makes the "every box in a subtree that
        /// never overrides an area ends up ReferenceEquals-sharing it" guarantee (see
        /// <see cref="ComputedStyle"/>'s remarks) actually hold, rather than only holding when a child
        /// happens to start from the shared <c>Default</c>.
        /// </summary>
        internal static TSelf AdoptArea<TSelf, TArea>(this TSelf self, TArea currentArea, TArea newArea, Func<TSelf, TArea, TSelf> apply)
            where TArea : class =>
            ReferenceEquals(currentArea, newArea) ? self : apply(self, newArea);
    }

    #region Border

    /// <summary>
    /// CSS Backgrounds and Borders Module properties (border widths/styles/colors/radii, box-shadow).
    /// None of these are inherited.
    /// </summary>
    internal sealed record BorderArea
    {
        internal static readonly BorderArea Default = new();

        public string BorderTopWidth { get; init; } = CssDefaults.GetInitialValue(PropertyNames.BorderTopWidth)!;
        public string BorderRightWidth { get; init; } = CssDefaults.GetInitialValue(PropertyNames.BorderRightWidth)!;
        public string BorderBottomWidth { get; init; } = CssDefaults.GetInitialValue(PropertyNames.BorderBottomWidth)!;
        public string BorderLeftWidth { get; init; } = CssDefaults.GetInitialValue(PropertyNames.BorderLeftWidth)!;

        public string BorderTopStyle { get; init; } = CssDefaults.GetInitialValue(PropertyNames.BorderTopStyle)!;
        public string BorderRightStyle { get; init; } = CssDefaults.GetInitialValue(PropertyNames.BorderRightStyle)!;
        public string BorderBottomStyle { get; init; } = CssDefaults.GetInitialValue(PropertyNames.BorderBottomStyle)!;
        public string BorderLeftStyle { get; init; } = CssDefaults.GetInitialValue(PropertyNames.BorderLeftStyle)!;

        public string BorderTopColor { get; init; } = CssDefaults.GetInitialValue(PropertyNames.BorderTopColor)!;
        public string BorderRightColor { get; init; } = CssDefaults.GetInitialValue(PropertyNames.BorderRightColor)!;
        public string BorderBottomColor { get; init; } = CssDefaults.GetInitialValue(PropertyNames.BorderBottomColor)!;
        public string BorderLeftColor { get; init; } = CssDefaults.GetInitialValue(PropertyNames.BorderLeftColor)!;

        public string BorderTopLeftRadius { get; init; } = CssDefaults.GetInitialValue(PropertyNames.BorderTopLeftRadius)!;
        public string BorderTopRightRadius { get; init; } = CssDefaults.GetInitialValue(PropertyNames.BorderTopRightRadius)!;
        public string BorderBottomRightRadius { get; init; } = CssDefaults.GetInitialValue(PropertyNames.BorderBottomRightRadius)!;
        public string BorderBottomLeftRadius { get; init; } = CssDefaults.GetInitialValue(PropertyNames.BorderBottomLeftRadius)!;

        // css-backgrounds-3 groups box-shadow with border/radius.
        public string BoxShadow { get; init; } = CssDefaults.GetInitialValue(PropertyNames.BoxShadow)!;
    }

    #endregion

    #region VisualEffects

    /// <summary>
    /// CSS Transforms, Compositing, Masking, and Box Sizing (aspect-ratio) properties. None inherited.
    /// </summary>
    internal sealed record VisualEffectsArea
    {
        internal static readonly VisualEffectsArea Default = new();

        public string Transform { get; init; } = CssDefaults.GetInitialValue(PropertyNames.Transform)!;
        public string TransformOrigin { get; init; } = CssDefaults.GetInitialValue(PropertyNames.TransformOrigin)!;
        public string Opacity { get; init; } = CssDefaults.GetInitialValue(PropertyNames.Opacity)!;
        public string ClipPath { get; init; } = CssDefaults.GetInitialValue(PropertyNames.ClipPath)!;
        public string AspectRatio { get; init; } = CssDefaults.GetInitialValue(PropertyNames.AspectRatio)!;
    }

    #endregion

    #region GeneratedContent

    /// <summary>
    /// CSS Generated Content / Lists and Counters properties, plus PeachPDF's own tagged-PDF extension.
    /// None inherited.
    /// </summary>
    internal sealed record GeneratedContentArea
    {
        internal static readonly GeneratedContentArea Default = new();

        public string Content { get; init; } = CssDefaults.GetInitialValue(PropertyNames.Content)!;
        public string CounterIncrement { get; init; } = CssDefaults.GetInitialValue(PropertyNames.CounterIncrement)!;
        public string CounterReset { get; init; } = CssDefaults.GetInitialValue(PropertyNames.CounterReset)!;
        // No PropertyNames constant for counter-set; use the literal CSS name.
        public string CounterSet { get; init; } = CssDefaults.GetInitialValue("counter-set")!;
        public string StringSet { get; init; } = CssDefaults.GetInitialValue(PropertyNames.StringSet)!;
        public string PageName { get; init; } = CssDefaults.GetInitialValue(PropertyNames.PageName)!;
        public string PdfTagType { get; init; } = CssDefaults.GetInitialValue(PropertyNames.PdfTagType)!;
    }

    #endregion

    #region BoxModel

    /// <summary>
    /// CSS Box Model + Position (offsets) + Box Sizing (box-sizing) properties. None inherited -
    /// <c>box-sizing</c> lives here (not a separate area) precisely because CSS Box Sizing 3 defines it
    /// as <c>inherited: no</c>, unlike the rest of this codebase's former treatment of it.
    /// </summary>
    internal sealed record BoxModelArea
    {
        internal static readonly BoxModelArea Default = new();

        public string MarginTop { get; init; } = CssDefaults.GetInitialValue(PropertyNames.MarginTop)!;
        public string MarginRight { get; init; } = CssDefaults.GetInitialValue(PropertyNames.MarginRight)!;
        public string MarginBottom { get; init; } = CssDefaults.GetInitialValue(PropertyNames.MarginBottom)!;
        public string MarginLeft { get; init; } = CssDefaults.GetInitialValue(PropertyNames.MarginLeft)!;

        public string PaddingTop { get; init; } = CssDefaults.GetInitialValue(PropertyNames.PaddingTop)!;
        public string PaddingRight { get; init; } = CssDefaults.GetInitialValue(PropertyNames.PaddingRight)!;
        public string PaddingBottom { get; init; } = CssDefaults.GetInitialValue(PropertyNames.PaddingBottom)!;
        public string PaddingLeft { get; init; } = CssDefaults.GetInitialValue(PropertyNames.PaddingLeft)!;

        public string Left { get; init; } = CssDefaults.GetInitialValue(PropertyNames.Left)!;
        public string Top { get; init; } = CssDefaults.GetInitialValue(PropertyNames.Top)!;
        public string Bottom { get; init; } = CssDefaults.GetInitialValue(PropertyNames.Bottom)!;
        public string Right { get; init; } = CssDefaults.GetInitialValue(PropertyNames.Right)!;

        public string Width { get; init; } = CssDefaults.GetInitialValue(PropertyNames.Width)!;
        public string MaxWidth { get; init; } = CssDefaults.GetInitialValue(PropertyNames.MaxWidth)!;
        public string MinWidth { get; init; } = CssDefaults.GetInitialValue(PropertyNames.MinWidth)!;

        public string Height { get; init; } = CssDefaults.GetInitialValue(PropertyNames.Height)!;
        public string MaxHeight { get; init; } = CssDefaults.GetInitialValue(PropertyNames.MaxHeight)!;
        public string MinHeight { get; init; } = CssDefaults.GetInitialValue(PropertyNames.MinHeight)!;

        public CssProperty<BoxSizingMode> BoxSizing { get; init; } =
            CssProperty<BoxSizingMode>.FromCssText(CssDefaults.GetInitialValue(PropertyNames.BoxSizing)!, Map.BoxSizingModes, BoxSizingMode.ContentBox);
    }

    #endregion

    #region Break

    /// <summary>
    /// CSS Fragmentation Module properties that do NOT inherit (contrast <see cref="PaginationArea"/>,
    /// which holds the two that do).
    /// </summary>
    internal sealed record BreakArea
    {
        internal static readonly BreakArea Default = new();

        public string BreakBefore { get; init; } = CssDefaults.GetInitialValue(PropertyNames.BreakBefore)!;
        public string BreakInside { get; init; } = CssDefaults.GetInitialValue(PropertyNames.BreakInside)!;
        public string BreakAfter { get; init; } = CssDefaults.GetInitialValue(PropertyNames.BreakAfter)!;
        public CssProperty<BoxDecorationBreakMode> BoxDecorationBreak { get; init; } =
            CssProperty<BoxDecorationBreakMode>.FromCssText(CssDefaults.GetInitialValue(PropertyNames.BoxDecorationBreak)!, Map.BoxDecorationBreakModes, BoxDecorationBreakMode.Slice);
    }

    #endregion

    #region Pagination

    /// <summary>
    /// The two CSS Fragmentation Module properties that DO inherit (contrast <see cref="BreakArea"/>).
    /// </summary>
    internal sealed record PaginationArea
    {
        internal static readonly PaginationArea Default = new();

        public string Orphans { get; init; } = CssDefaults.GetInitialValue(PropertyNames.Orphans)!;
        public string Widows { get; init; } = CssDefaults.GetInitialValue(PropertyNames.Widows)!;
    }

    #endregion

    #region Background

    /// <summary>
    /// CSS Backgrounds Module properties, plus CSS Images' object-fit/object-position (grouped here for
    /// convenience - both concern how replaced content fills a box). None inherited.
    /// </summary>
    internal sealed record BackgroundArea
    {
        internal static readonly BackgroundArea Default = new();

        public string BackgroundColor { get; init; } = CssDefaults.GetInitialValue(PropertyNames.BackgroundColor)!;
        public IReadOnlyList<CssImage>? BackgroundImages { get; init; }
        public string BackgroundPosition { get; init; } = CssDefaults.GetInitialValue(PropertyNames.BackgroundPosition)!;
        public string BackgroundRepeat { get; init; } = CssDefaults.GetInitialValue(PropertyNames.BackgroundRepeat)!;
        public string BackgroundSize { get; init; } = CssDefaults.GetInitialValue(PropertyNames.BackgroundSize)!;
        public string BackgroundOrigin { get; init; } = CssDefaults.GetInitialValue(PropertyNames.BackgroundOrigin)!;
        public string BackgroundClip { get; init; } = CssDefaults.GetInitialValue(PropertyNames.BackgroundClip)!;
        public string BackgroundAttachment { get; init; } = CssDefaults.GetInitialValue(PropertyNames.BackgroundAttachment)!;
        public string ObjectFit { get; init; } = CssDefaults.GetInitialValue(PropertyNames.ObjectFit)!;
        public string ObjectPosition { get; init; } = CssDefaults.GetInitialValue(PropertyNames.ObjectPosition)!;
    }

    #endregion

    #region DisplayPositioning

    /// <summary>
    /// CSS Display + Positioned Layout + Overflow properties. Kept together (rather than split further
    /// by literal spec module) since <c>Display</c>'s used-value getter already reads <c>Float</c>
    /// directly for CSS2.1 §9.7 blockification. None inherited.
    /// </summary>
    internal sealed record DisplayPositioningArea
    {
        internal static readonly DisplayPositioningArea Default = new();

        public string Display { get; init; } = CssDefaults.GetInitialValue(PropertyNames.Display)!;
        public string Float { get; init; } = CssDefaults.GetInitialValue(PropertyNames.Float)!;
        public string Clear { get; init; } = CssDefaults.GetInitialValue(PropertyNames.Clear)!;
        public string Position { get; init; } = CssDefaults.GetInitialValue(PropertyNames.Position)!;
        // Issue #598's follow-up: typed union storage (CssKeywordOrValue<AutoKeyword, int>) instead of a
        // raw string, same mechanism as Direction/UnicodeBidi/WritingMode below.
        public CssProperty<CssKeywordOrValue<AutoKeyword, int>> ZIndex { get; init; } =
            CssKeywordOrValueParser.FromCssText<AutoKeyword, int>(
                CssDefaults.GetInitialValue(PropertyNames.ZIndex)!, Map.AutoKeywords, int.TryParse, AutoKeyword.Auto);
        public string Overflow { get; init; } = CssDefaults.GetInitialValue(PropertyNames.Overflow)!;

        // ContainerType carries its parsed enum through the cascade (CssProperty<T>), same as
        // Direction/UnicodeBidi/WritingMode in the Text area below.
        public CssProperty<ContainerTypeEnum> ContainerType { get; init; } =
            CssProperty<ContainerTypeEnum>.FromCssText(CssDefaults.GetInitialValue(PropertyNames.ContainerType)!, Map.ContainerTypes, ContainerTypeEnum.Normal);
        public string ContainerName { get; init; } = CssDefaults.GetInitialValue(PropertyNames.ContainerName)!;
    }

    #endregion

    #region Table

    /// <summary>
    /// CSS Tables Module properties that inherit (contrast most Border-area properties, which don't).
    /// </summary>
    internal sealed record TableArea
    {
        internal static readonly TableArea Default = new();

        public string BorderCollapse { get; init; } = CssDefaults.GetInitialValue(PropertyNames.BorderCollapse)!;
        public string BorderSpacing { get; init; } = CssDefaults.GetInitialValue(PropertyNames.BorderSpacing)!;
        public string EmptyCells { get; init; } = CssDefaults.GetInitialValue(PropertyNames.EmptyCells)!;
    }

    #endregion

    #region Text

    /// <summary>
    /// CSS Color + Text + Writing Modes properties that inherit. <c>text-decoration-*</c> is
    /// deliberately excluded - see <see cref="TextDecorationArea"/>.
    /// <para>
    /// Two exceptions to "all inherit": <see cref="VerticalAlign"/> (CSS 2.1 §10.8.1) and
    /// <see cref="UnicodeBidi"/> (CSS Writing Modes 4) are both CSS-spec <c>Inherited: no</c>, correctly -
    /// neither is listed in <c>CssDefaults.InheritedProperties</c>. Since the whole-area adoption below
    /// unconditionally copies every property in this area from the parent, <c>CssBox.InheritStyle</c>
    /// explicitly restores both to this box's own pre-inherit value immediately after adopting the area
    /// (skipped for the `everything: true` structural-duplicate path, which keeps the adopted values on
    /// purpose - see the comment there; unlike <c>box-sizing</c>, no separate explicit copy is needed for
    /// that path, since skipping the restore already leaves both properties equal to the parent's). They
    /// live in this area, rather than their own non-inherited one, purely for textual convenience:
    /// <see cref="VerticalAlign"/> alongside the other text properties, and <see cref="UnicodeBidi"/> next
    /// to <c>direction</c>/<c>writing-mode</c> since they're the same CSS Writing Modes module.
    /// </para>
    /// </summary>
    internal sealed record TextArea
    {
        internal static readonly TextArea Default = new();

        public string Color { get; init; } = CssDefaults.GetInitialValue(PropertyNames.Color)!;
        public string LineHeight { get; init; } = CssDefaults.GetInitialValue(PropertyNames.LineHeight)!;
        public string VerticalAlign { get; init; } = CssDefaults.GetInitialValue(PropertyNames.VerticalAlign)!;
        public string TextIndent { get; init; } = CssDefaults.GetInitialValue(PropertyNames.TextIndent)!;
        public string TextAlign { get; init; } = CssDefaults.GetInitialValue(PropertyNames.TextAlign)!;
        public string TextTransform { get; init; } = CssDefaults.GetInitialValue(PropertyNames.TextTransform)!;
        public string WhiteSpace { get; init; } = CssDefaults.GetInitialValue(PropertyNames.WhiteSpace)!;
        public string Visibility { get; init; } = CssDefaults.GetInitialValue(PropertyNames.Visibility)!;
        public string WordSpacing { get; init; } = CssDefaults.GetInitialValue(PropertyNames.WordSpacing)!;
        public string LetterSpacing { get; init; } = CssDefaults.GetInitialValue(PropertyNames.LetterSpacing)!;
        public string WordBreak { get; init; } = CssDefaults.GetInitialValue(PropertyNames.WordBreak)!;

        // Direction/UnicodeBidi/WritingMode carry their parsed enum through the cascade (CssProperty<T>),
        // the same mechanism GridTemplateColumns/GridTemplateRows already use, rather than the raw string
        // every other property in this area uses - see CssProperty<T>.FromCssText.
        public CssProperty<DirectionMode> Direction { get; init; } =
            CssProperty<DirectionMode>.FromCssText(CssDefaults.GetInitialValue(PropertyNames.Direction)!, Map.DirectionModes, DirectionMode.Ltr);
        public CssProperty<UnicodeMode> UnicodeBidi { get; init; } =
            CssProperty<UnicodeMode>.FromCssText(CssDefaults.GetInitialValue(PropertyNames.UnicodeBidirectional)!, Map.UnicodeModes, UnicodeMode.Normal);
        public CssProperty<WritingModeEnum> WritingMode { get; init; } =
            CssProperty<WritingModeEnum>.FromCssText(CssDefaults.GetInitialValue(PropertyNames.WritingMode)!, Map.WritingModes, WritingModeEnum.HorizontalTb);

        public string Hyphens { get; init; } = CssDefaults.GetInitialValue(PropertyNames.Hyphens)!;
    }

    #endregion

    #region TextDecoration

    /// <summary>
    /// CSS Text Decoration Module properties. Unlike the rest of "Color &amp; Typography", these do NOT
    /// inherit through the normal CSS mechanism (they visually propagate via a separate, explicit
    /// mechanism in <c>DomParser</c>).
    /// </summary>
    internal sealed record TextDecorationArea
    {
        internal static readonly TextDecorationArea Default = new();

        public string TextDecorationLine { get; init; } = CssDefaults.GetInitialValue(PropertyNames.TextDecorationLine)!;
        public string TextDecorationStyle { get; init; } = CssDefaults.GetInitialValue(PropertyNames.TextDecorationStyle)!;
        public string TextDecorationColor { get; init; } = CssDefaults.GetInitialValue(PropertyNames.TextDecorationColor)!;
    }

    #endregion

    #region Font

    /// <summary>CSS Fonts Module properties. All inherit.</summary>
    internal sealed record FontArea
    {
        internal static readonly FontArea Default = new();

        // Null (not sourced from CssDefaults) is a deliberate "not yet resolved" sentinel -
        // DerivedStyle.ActualFont lazily falls back to CssConstants.DefaultFont when empty.
        public string? FontFamily { get; init; }

        // Not a real CSS property (no PropertyNames/CssDefaults entry) - a PeachPDF-internal
        // companion to FontFamily carrying the full unresolved font-family list.
        public string? FontFamilyList { get; init; }

        public string FontSize { get; init; } = CssDefaults.GetInitialValue(PropertyNames.FontSize)!;
        public string FontStyle { get; init; } = CssDefaults.GetInitialValue(PropertyNames.FontStyle)!;
        public string FontVariantCaps { get; init; } = CssDefaults.GetInitialValue(PropertyNames.FontVariantCaps)!;
        public string FontVariantLigatures { get; init; } = CssDefaults.GetInitialValue(PropertyNames.FontVariantLigatures)!;
        public string FontVariantNumeric { get; init; } = CssDefaults.GetInitialValue(PropertyNames.FontVariantNumeric)!;
        public string FontVariantEastAsian { get; init; } = CssDefaults.GetInitialValue(PropertyNames.FontVariantEastAsian)!;
        public string FontFeatureSettings { get; init; } = CssDefaults.GetInitialValue(PropertyNames.FontFeatureSettings)!;
        public string FontWeight { get; init; } = CssDefaults.GetInitialValue(PropertyNames.FontWeight)!;
        public string FontStretch { get; init; } = CssDefaults.GetInitialValue(PropertyNames.FontStretch)!;
        public string FontPalette { get; init; } = CssDefaults.GetInitialValue(PropertyNames.FontPalette)!;
    }

    #endregion

    #region List

    /// <summary>CSS Lists and Counters Module properties. All inherit.</summary>
    internal sealed record ListArea
    {
        internal static readonly ListArea Default = new();

        public string ListStylePosition { get; init; } = CssDefaults.GetInitialValue(PropertyNames.ListStylePosition)!;
        // Null (not "none") - a structured type, resolved via CssImagePainter's own parser, not the
        // string-only initial-value store.
        public CssImage? ListStyleImage { get; init; }
        public string ListStyleType { get; init; } = CssDefaults.GetInitialValue(PropertyNames.ListStyleType)!;
    }

    #endregion

    #region Flex

    /// <summary>CSS Flexible Box Layout Module properties (container + item). None inherited.</summary>
    internal sealed record FlexArea
    {
        internal static readonly FlexArea Default = new();

        public string FlexDirection { get; init; } = CssDefaults.GetInitialValue(PropertyNames.FlexDirection)!;
        public string FlexWrap { get; init; } = CssDefaults.GetInitialValue(PropertyNames.FlexWrap)!;
        public string JustifyContent { get; init; } = CssDefaults.GetInitialValue(PropertyNames.JustifyContent)!;
        public string AlignItems { get; init; } = CssDefaults.GetInitialValue(PropertyNames.AlignItems)!;
        public string AlignContent { get; init; } = CssDefaults.GetInitialValue(PropertyNames.AlignContent)!;

        public string FlexGrow { get; init; } = CssDefaults.GetInitialValue(PropertyNames.FlexGrow)!;
        public string FlexShrink { get; init; } = CssDefaults.GetInitialValue(PropertyNames.FlexShrink)!;
        public string FlexBasis { get; init; } = CssDefaults.GetInitialValue(PropertyNames.FlexBasis)!;
        public string AlignSelf { get; init; } = CssDefaults.GetInitialValue(PropertyNames.AlignSelf)!;
        public string Order { get; init; } = CssDefaults.GetInitialValue(PropertyNames.Order)!;

        // Shared with Grid/MultiColumn's row-gap/column-gap (the same CSS property); the C# name here
        // keeps its historical "Flex" prefix, but the CSS name is PropertyNames.RowGap/ColumnGap, not
        // "flex-row-gap"/"flex-column-gap".
        public string FlexRowGap { get; init; } = CssDefaults.GetInitialValue(PropertyNames.RowGap)!;
        public string FlexColumnGap { get; init; } = CssDefaults.GetInitialValue(PropertyNames.ColumnGap)!;
    }

    #endregion

    #region Grid

    /// <summary>CSS Grid Layout Module properties (container + item). None inherited.</summary>
    internal sealed record GridArea
    {
        internal static readonly GridArea Default = new();

        // grid-template-columns/rows carry the parsed GridTemplate through the cascade (CssProperty<T>)
        // so the layout engine reads the already-parsed value. The raw CSS text half of the initial
        // value ("none") still comes from CssDefaults; there is no cascade value to parse for "none",
        // so the parsed half stays a literal null.
        public CssProperty<GridTemplate> GridTemplateColumns { get; init; } =
            CssProperty<GridTemplate>.FromValue(CssDefaults.GetInitialValue(PropertyNames.GridTemplateColumns)!, null);
        public CssProperty<GridTemplate> GridTemplateRows { get; init; } =
            CssProperty<GridTemplate>.FromValue(CssDefaults.GetInitialValue(PropertyNames.GridTemplateRows)!, null);

        public string GridTemplateAreas { get; init; } = CssDefaults.GetInitialValue(PropertyNames.GridTemplateAreas)!;
        public string GridAutoColumns { get; init; } = CssDefaults.GetInitialValue(PropertyNames.GridAutoColumns)!;
        public string GridAutoRows { get; init; } = CssDefaults.GetInitialValue(PropertyNames.GridAutoRows)!;
        public string GridAutoFlow { get; init; } = CssDefaults.GetInitialValue(PropertyNames.GridAutoFlow)!;
        public string JustifyItems { get; init; } = CssDefaults.GetInitialValue(PropertyNames.JustifyItems)!;
        public string JustifySelf { get; init; } = CssDefaults.GetInitialValue(PropertyNames.JustifySelf)!;

        public string GridColumnStart { get; init; } = CssDefaults.GetInitialValue(PropertyNames.GridColumnStart)!;
        public string GridColumnEnd { get; init; } = CssDefaults.GetInitialValue(PropertyNames.GridColumnEnd)!;
        public string GridRowStart { get; init; } = CssDefaults.GetInitialValue(PropertyNames.GridRowStart)!;
        public string GridRowEnd { get; init; } = CssDefaults.GetInitialValue(PropertyNames.GridRowEnd)!;
    }

    #endregion

    #region MultiColumn

    /// <summary>CSS Multi-column Layout Module properties. None inherited.</summary>
    internal sealed record MultiColumnArea
    {
        internal static readonly MultiColumnArea Default = new();

        public string ColumnCount { get; init; } = CssDefaults.GetInitialValue(PropertyNames.ColumnCount)!;
        public string ColumnWidth { get; init; } = CssDefaults.GetInitialValue(PropertyNames.ColumnWidth)!;
        public CssProperty<ColumnFillMode> ColumnFill { get; init; } =
            CssProperty<ColumnFillMode>.FromCssText(CssDefaults.GetInitialValue(PropertyNames.ColumnFill)!, Map.ColumnFillModes, ColumnFillMode.Balance);
        public CssProperty<ColumnSpanMode> ColumnSpan { get; init; } =
            CssProperty<ColumnSpanMode>.FromCssText(CssDefaults.GetInitialValue(PropertyNames.ColumnSpan)!, Map.ColumnSpanModes, ColumnSpanMode.None);
        public string ColumnRuleWidth { get; init; } = CssDefaults.GetInitialValue(PropertyNames.ColumnRuleWidth)!;
        public string ColumnRuleStyle { get; init; } = CssDefaults.GetInitialValue(PropertyNames.ColumnRuleStyle)!;
        public string ColumnRuleColor { get; init; } = CssDefaults.GetInitialValue(PropertyNames.ColumnRuleColor)!;
    }

    #endregion
}
