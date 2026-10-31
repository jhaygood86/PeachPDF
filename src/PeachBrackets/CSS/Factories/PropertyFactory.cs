#nullable disable

using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;

namespace PeachBrackets.Css
{
    internal sealed partial class PropertyFactory
    {
        private static readonly Lazy<PropertyFactory> Lazy = new(() => new PropertyFactory());

        private readonly List<string> _animatables = new();

        private readonly Dictionary<string, LonghandCreator> _fontsBuilder = new(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, LonghandCreator> _longhandsBuilder = new(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, string[]> _mappingsBuilder = new();

        private readonly Dictionary<string, ShorthandCreator> _shorthandsBuilder = new(StringComparer.OrdinalIgnoreCase);

        // Shorthands that parse and expand like any other, but are NOT used to reconstruct a shorthand when
        // serializing a declaration block. Two kinds live here: the logical box shorthands (margin-block,
        // inset, border-inline, …), which alias the same physical longhands as their physical counterparts
        // (margin-top/-bottom/…) so letting the serializer collapse e.g. margin-top+margin-bottom into
        // `margin-block` would change existing output; and the grid mega-shorthands (grid, grid-template),
        // whose multi-slash / areas grammar isn't worth reconstructing. Excluded from GetShorthands (the
        // serialization query) only; CreateShorthand/GetLonghands/IsShorthand still work.
        private readonly HashSet<string> _logicalShorthands = new(StringComparer.OrdinalIgnoreCase);

        private readonly FrozenDictionary<string, LonghandCreator> _fonts;

        // @property descriptors (syntax / initial-value / inherits). Their values have no fixed CSS grammar
        // (initial-value depends on the syntax; syntax is an arbitrary string), so each is stored raw via an
        // UnknownProperty (Converters.Any) and validated later against the syntax string in Layer B.
        private readonly FrozenDictionary<string, LonghandCreator> _propertyDescriptors;

        // @font-palette-values descriptors (font-family / base-palette / override-colors). Stored raw via
        // UnknownProperty and validated later in Layer B (RegisteredFontPalette.FromRule).
        private readonly FrozenDictionary<string, LonghandCreator> _fontPaletteDescriptors;
        private readonly FrozenDictionary<string, LonghandCreator> _counterStyleDescriptors;

        private readonly FrozenDictionary<string, LonghandCreator> _longhands;

        private readonly FrozenDictionary<string, string[]> _mappings;

        private readonly FrozenDictionary<string, ShorthandCreator> _shorthands;

        private PropertyFactory()
        {
            RegisterGeneratedLonghands();

            AddLonghand(PropertyNames.AlignContent, () => new AlignContentProperty());
            AddLonghand(PropertyNames.AlignSelf, () => new AlignSelfProperty());
            AddShorthand(PropertyNames.Animation, () => new AnimationProperty(),
                PropertyNames.AnimationName,
                PropertyNames.AnimationDuration,
                PropertyNames.AnimationTimingFunction,
                PropertyNames.AnimationDelay,
                PropertyNames.AnimationDirection,
                PropertyNames.AnimationFillMode,
                PropertyNames.AnimationIterationCount,
                PropertyNames.AnimationPlayState);
            AddLonghand(PropertyNames.AnimationDelay, () => new AnimationDelayProperty());
            AddLonghand(PropertyNames.AnimationDirection, () => new AnimationDirectionProperty());
            AddLonghand(PropertyNames.AnimationDuration, () => new AnimationDurationProperty());
            AddLonghand(PropertyNames.AnimationFillMode, () => new AnimationFillModeProperty());
            AddLonghand(PropertyNames.AnimationIterationCount, () => new AnimationIterationCountProperty());
            AddLonghand(PropertyNames.AnimationName, () => new AnimationNameProperty());
            AddLonghand(PropertyNames.AnimationPlayState, () => new AnimationPlayStateProperty());
            AddLonghand(PropertyNames.AnimationTimingFunction, () => new AnimationTimingFunctionProperty());

            AddShorthand(PropertyNames.Background, () => new BackgroundProperty(),
                PropertyNames.BackgroundAttachment,
                PropertyNames.BackgroundClip,
                PropertyNames.BackgroundColor,
                PropertyNames.BackgroundImage,
                PropertyNames.BackgroundOrigin,
                PropertyNames.BackgroundPosition,
                PropertyNames.BackgroundRepeat,
                PropertyNames.BackgroundSize);
            AddLonghand(PropertyNames.BackgroundSize, () => new BackgroundSizeProperty(), true);
            AddLonghand(PropertyNames.BackgroundPosition, () => new BackgroundPositionProperty(), true);

            AddLonghand(PropertyNames.BoxSizing, () => new BoxSizingProperty());
            foreach (var legacyPrefix in LegacyBoxProperty.Prefixes)
            {
                foreach (var legacyBase in new[] { LegacyBoxProperty.Orient, LegacyBoxProperty.Direction, LegacyBoxProperty.Pack, LegacyBoxProperty.Align })
                {
                    var prefix = legacyPrefix;
                    var baseName = legacyBase;
                    AddLonghand(prefix + baseName, () => new LegacyBoxProperty(prefix, baseName));
                }
            }
            AddLonghand(PropertyNames.BreakAfter, () => new BreakAfterProperty());
            AddLonghand(PropertyNames.BreakBefore, () => new BreakBeforeProperty());

            AddShorthand(PropertyNames.BorderRadius, () => new BorderRadiusProperty(),
                PropertyNames.BorderTopLeftRadius,
                PropertyNames.BorderTopRightRadius,
                PropertyNames.BorderBottomRightRadius,
                PropertyNames.BorderBottomLeftRadius);
            AddLonghand(PropertyNames.BorderTopLeftRadius, () => new BorderTopLeftRadiusProperty(), true);
            AddLonghand(PropertyNames.BorderTopRightRadius, () => new BorderTopRightRadiusProperty(), true);
            AddLonghand(PropertyNames.BorderBottomLeftRadius, () => new BorderBottomLeftRadiusProperty(), true);
            AddLonghand(PropertyNames.BorderBottomRightRadius, () => new BorderBottomRightRadiusProperty(), true);

            AddShorthand(PropertyNames.BorderImage, () => new BorderImageProperty(),
                PropertyNames.BorderImageOutset,
                PropertyNames.BorderImageRepeat,
                PropertyNames.BorderImageSlice,
                PropertyNames.BorderImageSource,
                PropertyNames.BorderImageWidth);
            AddLonghand(PropertyNames.BorderImageOutset, () => new BorderImageOutsetProperty());
            AddLonghand(PropertyNames.BorderImageRepeat, () => new BorderImageRepeatProperty());
            AddLonghand(PropertyNames.BorderImageSlice, () => new BorderImageSliceProperty());
            AddLonghand(PropertyNames.BorderImageWidth, () => new BorderImageWidthProperty());

            AddShorthand(PropertyNames.BorderColor, () => new BorderColorProperty(),
                PropertyNames.BorderTopColor,
                PropertyNames.BorderRightColor,
                PropertyNames.BorderBottomColor,
                PropertyNames.BorderLeftColor);
            AddShorthand(PropertyNames.BorderStyle, () => new BorderStyleProperty(),
                PropertyNames.BorderTopStyle,
                PropertyNames.BorderRightStyle,
                PropertyNames.BorderBottomStyle,
                PropertyNames.BorderLeftStyle);
            AddShorthand(PropertyNames.BorderWidth, () => new BorderWidthProperty(),
                PropertyNames.BorderTopWidth,
                PropertyNames.BorderRightWidth,
                PropertyNames.BorderBottomWidth,
                PropertyNames.BorderLeftWidth);
            AddShorthand(PropertyNames.BorderTop, () => new BorderTopProperty(),
                PropertyNames.BorderTopWidth,
                PropertyNames.BorderTopStyle,
                PropertyNames.BorderTopColor);
            AddShorthand(PropertyNames.BorderRight, () => new BorderRightProperty(),
                PropertyNames.BorderRightWidth,
                PropertyNames.BorderRightStyle,
                PropertyNames.BorderRightColor);
            AddShorthand(PropertyNames.BorderBottom, () => new BorderBottomProperty(),
                PropertyNames.BorderBottomWidth,
                PropertyNames.BorderBottomStyle,
                PropertyNames.BorderBottomColor);
            AddShorthand(PropertyNames.BorderLeft, () => new BorderLeftProperty(),
                PropertyNames.BorderLeftWidth,
                PropertyNames.BorderLeftStyle,
                PropertyNames.BorderLeftColor);

            AddShorthand(PropertyNames.Border, () => new BorderProperty(),
                PropertyNames.BorderTopWidth,
                PropertyNames.BorderTopStyle,
                PropertyNames.BorderTopColor,
                PropertyNames.BorderRightWidth,
                PropertyNames.BorderRightStyle,
                PropertyNames.BorderRightColor,
                PropertyNames.BorderBottomWidth,
                PropertyNames.BorderBottomStyle,
                PropertyNames.BorderBottomColor,
                PropertyNames.BorderLeftWidth,
                PropertyNames.BorderLeftStyle,
                PropertyNames.BorderLeftColor);
            AddLonghand(PropertyNames.BorderTopStyle, () => new BorderTopStyleProperty());
            AddLonghand(PropertyNames.BorderLeftStyle, () => new BorderLeftStyleProperty());
            AddLonghand(PropertyNames.BorderRightStyle, () => new BorderRightStyleProperty());
            AddLonghand(PropertyNames.BorderBottomStyle, () => new BorderBottomStyleProperty());


            AddShorthand(PropertyNames.Columns, () => new ColumnsProperty(),
                PropertyNames.ColumnWidth,
                PropertyNames.ColumnCount);
            AddLonghand(PropertyNames.ColumnWidth, () => new ColumnWidthProperty(), true);



            AddShorthand(PropertyNames.GridColumn, () => new GridColumnProperty(),
                PropertyNames.GridColumnStart, PropertyNames.GridColumnEnd);
            AddShorthand(PropertyNames.GridRow, () => new GridRowProperty(),
                PropertyNames.GridRowStart, PropertyNames.GridRowEnd);
            AddShorthand(PropertyNames.GridArea, () => new GridAreaProperty(),
                PropertyNames.GridRowStart, PropertyNames.GridColumnStart,
                PropertyNames.GridRowEnd, PropertyNames.GridColumnEnd);


            // The grid mega-shorthands parse/expand like any other, but are excluded from serialization
            // reconstruction (via _logicalShorthands) - reconstructing a `grid`/`grid-template` from its
            // longhands is not worth the complexity and could change existing output.
            AddLogicalShorthand(PropertyNames.GridTemplate, () => new GridTemplateProperty(),
                PropertyNames.GridTemplateRows, PropertyNames.GridTemplateColumns, PropertyNames.GridTemplateAreas);
            AddLogicalShorthand(PropertyNames.Grid, () => new GridProperty(),
                PropertyNames.GridTemplateRows, PropertyNames.GridTemplateColumns, PropertyNames.GridTemplateAreas,
                PropertyNames.GridAutoFlow, PropertyNames.GridAutoRows, PropertyNames.GridAutoColumns);

            AddLonghand(PropertyNames.JustifyItems, () => new JustifyItemsProperty());
            AddLonghand(PropertyNames.JustifySelf, () => new JustifySelfProperty());
            AddShorthand(PropertyNames.PlaceItems, () => new PlaceItemsProperty(),
                PropertyNames.AlignItems, PropertyNames.JustifyItems);
            AddShorthand(PropertyNames.PlaceContent, () => new PlaceContentProperty(),
                PropertyNames.AlignContent, PropertyNames.JustifyContent);
            AddShorthand(PropertyNames.PlaceSelf, () => new PlaceSelfProperty(),
                PropertyNames.AlignSelf, PropertyNames.JustifySelf);

            AddShorthand(PropertyNames.ColumnRule, () => new ColumnRuleProperty(),
                PropertyNames.ColumnRuleWidth,
                PropertyNames.ColumnRuleStyle,
                PropertyNames.ColumnRuleColor);
            AddLonghand(PropertyNames.ColumnRuleStyle, () => new ColumnRuleStyleProperty());

            AddLonghand(PropertyNames.Clear, () => new ClearProperty());
            AddShorthand(PropertyNames.Container, () => new ContainerProperty(),
                PropertyNames.ContainerName, PropertyNames.ContainerType);
            AddLonghand(PropertyNames.ContainerName, () => new ContainerNameProperty());
            AddLonghand(PropertyNames.Content, () => new ContentProperty());
            AddLonghand(PropertyNames.CounterIncrement, () => new CounterIncrementProperty());
            AddLonghand(PropertyNames.CounterReset, () => new CounterResetProperty());
            AddLonghand(PropertyNames.CounterSet, () => new CounterSetProperty());
            AddLonghand(PropertyNames.Cursor, () => new CursorProperty());
            AddLonghand(PropertyNames.Fill, () => new FillProperty(), true);
            AddLonghand(PropertyNames.FillRule, () => new FillRuleProperty(), true);
            AddShorthand(PropertyNames.Flex, () => new FlexProperty(),
                PropertyNames.FlexGrow,
                PropertyNames.FlexShrink,
                PropertyNames.FlexBasis);
            AddLonghand(PropertyNames.FlexBasis, () => new FlexBasisProperty(), true);
            AddLonghand(PropertyNames.FlexDirection, () => new FlexDirectionProperty());
            AddShorthand(PropertyNames.FlexFlow, () => new FlexFlowProperty(),
                PropertyNames.FlexDirection,
                PropertyNames.FlexWrap);
            AddLonghand(PropertyNames.FlexGrow, () => new FlexGrowProperty());
            AddLonghand(PropertyNames.FlexShrink, () => new FlexShrinkProperty());
            AddLonghand(PropertyNames.FlexWrap, () => new FlexWrapProperty());

            // CSS Fonts 4 §7.7 "Reset Implicitly": the font-variant-* longhands (and
            // font-feature-settings) reset to their initial value whenever `font` is set, even though
            // none of them can be written as part of `font`'s own shorthand syntax -
            // ShorthandProperty.Export does exactly that for any longhand in this list the grammar
            // didn't extract a value for. font-size-adjust/font-palette are on the same spec list but
            // aren't listed here yet (pre-existing, not introduced by this change).
            AddShorthand(PropertyNames.Font, () => new FontProperty(),
                PropertyNames.FontFamily,
                PropertyNames.FontSize,
                PropertyNames.FontStretch,
                PropertyNames.FontStyle,
                PropertyNames.FontVariantCaps,
                PropertyNames.FontVariantLigatures,
                PropertyNames.FontVariantNumeric,
                PropertyNames.FontVariantEastAsian,
                PropertyNames.FontVariantPosition,
                PropertyNames.FontVariantEmoji,
                PropertyNames.FontVariantAlternates,
                PropertyNames.FontKerning,
                PropertyNames.FontOpticalSizing,
                PropertyNames.FontVariationSettings,
                PropertyNames.FontWeight,
                PropertyNames.LineHeight);
            AddLonghand(PropertyNames.FontSizeAdjust, () => new FontSizeAdjustProperty(), true);
            AddShorthand(PropertyNames.FontSynthesis, () => new FontSynthesisProperty(),
                PropertyNames.FontSynthesisWeight,
                PropertyNames.FontSynthesisStyle,
                PropertyNames.FontSynthesisSmallCaps,
                PropertyNames.FontSynthesisPosition);
            // font-variant is a real shorthand (see FontVariantProperty) over the 4 longhands below
            // plus font-feature-settings; the @font-face `font-variant` descriptor is a separate,
            // unrelated, never-cascaded registration (FontFaceVariantProperty, added to _fontsBuilder
            // directly below, since AddShorthand has no `font: true` flag).
            AddShorthand(PropertyNames.FontVariant, () => new FontVariantProperty(),
                PropertyNames.FontVariantCaps,
                PropertyNames.FontVariantLigatures,
                PropertyNames.FontVariantNumeric,
                PropertyNames.FontVariantEastAsian,
                PropertyNames.FontVariantPosition,
                PropertyNames.FontVariantEmoji,
                PropertyNames.FontVariantAlternates,
                PropertyNames.FontFeatureSettings);
            AddLonghand(PropertyNames.FontVariantAlternates, () => new FontVariantAlternatesProperty());
            AddLonghand(PropertyNames.FontFeatureSettings, () => new FontFeatureSettingsProperty());
            AddLonghand(PropertyNames.FontLanguageOverride, () => new FontLanguageOverrideProperty());
            AddShorthand(PropertyNames.WebkitTextStroke, () => new WebkitTextStrokeProperty(),
                PropertyNames.WebkitTextStrokeWidth,
                PropertyNames.WebkitTextStrokeColor);
            AddLonghand(PropertyNames.FontVariationSettings, () => new FontVariationSettingsProperty());

            AddShorthand(PropertyNames.Gap, () => new GapProperty(),
                PropertyNames.RowGap,
                PropertyNames.ColumnGap);

            AddLonghand(PropertyNames.PrinceHyphenateCharacter, () => new PrinceHyphenateCharacterProperty());
            AddLonghand(PropertyNames.HyphenateLines, () => new HyphenateLinesProperty());
            AddLonghand(PropertyNames.PrinceHyphenateLimitLines, () => new PrinceHyphenateLimitLinesProperty());
            AddLonghand(PropertyNames.HyphenateBefore, () => new HyphenateBeforeProperty());
            AddLonghand(PropertyNames.PrinceHyphenateBefore, () => new PrinceHyphenateBeforeProperty());
            AddLonghand(PropertyNames.HyphenateAfter, () => new HyphenateAfterProperty());
            AddLonghand(PropertyNames.PrinceHyphenateAfter, () => new PrinceHyphenateAfterProperty());



            AddShorthand(PropertyNames.ListStyle, () => new ListStyleProperty(),
                PropertyNames.ListStyleType,
                PropertyNames.ListStyleImage,
                PropertyNames.ListStylePosition);
            AddLonghand(PropertyNames.ListStyleType, () => new ListStyleTypeProperty());

            AddShorthand(PropertyNames.Margin, () => new MarginProperty(),
                PropertyNames.MarginTop,
                PropertyNames.MarginRight,
                PropertyNames.MarginBottom,
                PropertyNames.MarginLeft);

            AddLonghand(PropertyNames.MaxHeight, () => new MaxHeightProperty(), true);
            AddLonghand(PropertyNames.MaxWidth, () => new MaxWidthProperty(), true);
            AddLonghand(PropertyNames.Opacity, () => new OpacityProperty(), true);

            AddShorthand(PropertyNames.Outline, () => new OutlineProperty(),
                PropertyNames.OutlineWidth,
                PropertyNames.OutlineStyle,
                PropertyNames.OutlineColor);
            AddLonghand(PropertyNames.OutlineStyle, () => new OutlineStyleProperty());

            AddShorthand(PropertyNames.Overflow, () => new OverflowProperty(),
                PropertyNames.OverflowX,
                PropertyNames.OverflowY);
            AddLonghand(PropertyNames.OverflowX, () => new OverflowXProperty());
            AddLonghand(PropertyNames.OverflowY, () => new OverflowYProperty());
            AddLonghand(PropertyNames.OverflowWrap, () => new OverflowWrapProperty());

            AddShorthand(PropertyNames.Padding, () => new PaddingProperty(),
                PropertyNames.PaddingTop,
                PropertyNames.PaddingRight,
                PropertyNames.PaddingBottom,
                PropertyNames.PaddingLeft);

            AddLonghand(PropertyNames.Perspective, () => new PerspectiveProperty(), true);
            AddLonghand(PropertyNames.PerspectiveOrigin, () => new PerspectiveOriginProperty(), true);
            AddLonghand(PropertyNames.Quotes, () => new QuotesProperty());
            AddLonghand(PropertyNames.FloodColor, () => new SvgOnlyProperty(PropertyNames.FloodColor, Converters.CurrentColorConverter.OrGlobalValue()), true);
            AddLonghand(PropertyNames.LightingColor, () => new SvgOnlyProperty(PropertyNames.LightingColor, Converters.CurrentColorConverter.OrGlobalValue()), true);
            AddLonghand(PropertyNames.FloodOpacity, () => new SvgOnlyProperty(PropertyNames.FloodOpacity, Converters.NumberConverter.Or(Converters.PercentConverter).OrDefault(1f)), true);
            AddLonghand(PropertyNames.ClipRule, () => new SvgOnlyProperty(PropertyNames.ClipRule, Converters.FillRuleConverter.OrDefault(FillRule.Nonzero)), true);
            AddLonghand(PropertyNames.Marker, () => new SvgOnlyProperty(PropertyNames.Marker, SvgOnlyProperty.MarkerReference), true);
            AddLonghand(PropertyNames.MarkerStart, () => new SvgOnlyProperty(PropertyNames.MarkerStart, SvgOnlyProperty.MarkerReference), true);
            AddLonghand(PropertyNames.MarkerMid, () => new SvgOnlyProperty(PropertyNames.MarkerMid, SvgOnlyProperty.MarkerReference), true);
            AddLonghand(PropertyNames.MarkerEnd, () => new SvgOnlyProperty(PropertyNames.MarkerEnd, SvgOnlyProperty.MarkerReference), true);
            AddLonghand(PropertyNames.Mask, () => new SvgOnlyProperty(PropertyNames.Mask, Converters.Any), true);
            AddLonghand(PropertyNames.PaintOrder, () => new SvgOnlyProperty(PropertyNames.PaintOrder, Converters.Any), true);
            AddLonghand(PropertyNames.ColorInterpolationFilters, () => new SvgOnlyProperty(PropertyNames.ColorInterpolationFilters, Converters.Any), true);
            AddLonghand(PropertyNames.TransformBox, () => new SvgOnlyProperty(PropertyNames.TransformBox, Converters.Any), true);
            AddLonghand(PropertyNames.AlignBaseline, () => new SvgOnlyProperty(PropertyNames.AlignBaseline, Converters.Any), true);
            AddLonghand(PropertyNames.DominantBaseline, () => new SvgOnlyProperty(PropertyNames.DominantBaseline, Converters.Any), true);
            AddLonghand(PropertyNames.BaselineShift, () => new SvgOnlyProperty(PropertyNames.BaselineShift, Converters.Any), true);
            AddLonghand(PropertyNames.InlineSize, () => new SvgOnlyProperty(PropertyNames.InlineSize, Converters.Any), true);
            AddLonghand(PropertyNames.ShapeInside, () => new SvgOnlyProperty(PropertyNames.ShapeInside, Converters.Any), true);
            AddLonghand(PropertyNames.ShapeSubtract, () => new SvgOnlyProperty(PropertyNames.ShapeSubtract, Converters.Any), true);
            AddLonghand(PropertyNames.R, () => new SvgOnlyProperty(PropertyNames.R, SvgOnlyProperty.GeometryLength), true);
            AddLonghand(PropertyNames.Cx, () => new SvgOnlyProperty(PropertyNames.Cx, SvgOnlyProperty.GeometryLength), true);
            AddLonghand(PropertyNames.Cy, () => new SvgOnlyProperty(PropertyNames.Cy, SvgOnlyProperty.GeometryLength), true);
            AddLonghand(PropertyNames.Rx, () => new SvgOnlyProperty(PropertyNames.Rx, SvgOnlyProperty.GeometryLength), true);
            AddLonghand(PropertyNames.Ry, () => new SvgOnlyProperty(PropertyNames.Ry, SvgOnlyProperty.GeometryLength), true);
            AddLonghand(PropertyNames.X, () => new SvgOnlyProperty(PropertyNames.X, SvgOnlyProperty.GeometryLength), true);
            AddLonghand(PropertyNames.Y, () => new SvgOnlyProperty(PropertyNames.Y, SvgOnlyProperty.GeometryLength), true);
            AddLonghand(PropertyNames.D, () => new SvgOnlyProperty(PropertyNames.D, Converters.Any), true);
            AddLonghand(PropertyNames.StopColor, () => new StopColorProperty(), true);
            AddLonghand(PropertyNames.Stroke, () => new StrokeProperty(), true);
            AddLonghand(PropertyNames.StrokeDasharray, () => new StrokeDasharrayProperty(), true);
            AddLonghand(PropertyNames.StrokeDashoffset, () => new StrokeDashoffsetProperty(), true);
            AddLonghand(PropertyNames.StrokeLinecap, () => new StrokeLinecapProperty(), true);
            AddLonghand(PropertyNames.StrokeLinejoin, () => new StrokeLinejoinProperty(), true);
            AddLonghand(PropertyNames.StrokeMiterlimit, () => new StrokeMiterlimitProperty(), true);
            AddLonghand(PropertyNames.StrokeWidth, () => new StrokeWidthProperty(), true);
            AddLonghand(PropertyNames.StringSet, () => new StringSetProperty());
            AddLonghand(PropertyNames.PageName, () => new PageNameProperty());
            AddLonghand(PropertyNames.PdfTagType, () => new PdfTagTypeProperty());
            AddLonghand(PropertyNames.PdfFormField, () => new PdfFormFieldProperty());
            AddLonghand(PropertyNames.PdfFormFieldAutoFontSize, () => new PdfFormFieldAutoFontSizeProperty());
            AddLonghand(PropertyNames.PdfFormFieldComb, () => new PdfFormFieldCombProperty());
            AddLonghand(PropertyNames.PdfFormFieldDoNotScroll, () => new PdfFormFieldDoNotScrollProperty());
            AddShorthand(PropertyNames.PrincePdfFormFieldSettings, () => new PrincePdfFormFieldSettingsProperty(),
                PropertyNames.PdfFormField, PropertyNames.PdfFormFieldAutoFontSize, PropertyNames.PdfFormFieldComb, PropertyNames.PdfFormFieldDoNotScroll);
            AddLonghand(PropertyNames.BookmarkLevel, () => new BookmarkLevelProperty());
            AddLonghand(PropertyNames.BookmarkState, () => new BookmarkStateProperty());
            AddLonghand(PropertyNames.PeachPdfBookmarkTarget, () => new BookmarkTargetProperty());
            AddLonghand(PropertyNames.PrinceBookmarkLevel, () => new PrinceBookmarkLevelProperty());
            AddLonghand(PropertyNames.PrinceBookmarkState, () => new PrinceBookmarkStateProperty());
            AddLonghand(PropertyNames.PrinceBookmarkTarget, () => new PrinceBookmarkTargetProperty());
            AddLonghand(PropertyNames.BookmarkTarget, () => new BookmarkTargetAliasProperty());
            AddLonghand(PropertyNames.TabSize, () => new TabSizeProperty());
            AddLonghand(PropertyNames.TextAlignLast, () => new TextAlignLastProperty());
            AddShorthand(PropertyNames.TextAlign, () => new TextAlignProperty(),
                PropertyNames.TextAlignAll, PropertyNames.TextAlignLast);
            AddLonghand(PropertyNames.TextAnchor, () => new TextAnchorProperty());

            AddShorthand(PropertyNames.TextDecoration, () => new TextDecorationProperty(),
                PropertyNames.TextDecorationLine,
                PropertyNames.TextDecorationStyle,
                PropertyNames.TextDecorationColor,
                PropertyNames.TextDecorationThickness);
            // Not part of the text-decoration shorthand (CSS Text Decoration 4 doesn't add it there).

            AddLonghand(PropertyNames.TextJustify, () => new TextJustifyProperty());
            AddLonghand(PropertyNames.TransformOrigin, () => new TransformOriginProperty(), true);

            AddShorthand(PropertyNames.Transition, () => new TransitionProperty(),
                PropertyNames.TransitionProperty,
                PropertyNames.TransitionDuration,
                PropertyNames.TransitionTimingFunction,
                PropertyNames.TransitionDelay);
            AddLonghand(PropertyNames.TransitionDelay, () => new TransitionDelayProperty());
            AddLonghand(PropertyNames.TransitionDuration, () => new TransitionDurationProperty());
            AddLonghand(PropertyNames.TransitionTimingFunction, () => new TransitionTimingFunctionProperty());
            AddLonghand(PropertyNames.TransitionProperty, () => new TransitionPropertyProperty());

            AddLonghand(PropertyNames.LineBreak, () => new LineBreakProperty(), true);
            AddLonghand(PropertyNames.WordBreak, () => new WordBreakProperty(), true);
            AddLonghand(PropertyNames.WordSpacing, () => new WordSpacingProperty(), true);
            AddLonghand(PropertyNames.Size, () => new PageSizeProperty());

            // CSS Logical Properties and Values Level 1. Each logical longhand is its own genuine,
            // distinctly-identified Property (CSS/StyleProperties/Logical/) rather than an alias onto a
            // physical one - CssBox.CascadeApplyStyles resolves each to its physical edge afterward, via
            // LogicalPropertyResolver, against the box's own resolved direction/writing-mode (CSS Writing
            // Modes 3's abstract-to-physical mapping table), so `margin-inline-start` genuinely means
            // "insertion edge" rather than always `margin-left`. Shorthands expand into the *logical*
            // longhand names below (not their physical counterparts) for the same reason.

            // Logical margin longhands.
            // Logical margin shorthands.
            AddLogicalShorthand(PropertyNames.MarginBlock, () => new MarginBlockProperty(),
                PropertyNames.MarginBlockStart,
                PropertyNames.MarginBlockEnd);
            AddLogicalShorthand(PropertyNames.MarginInline, () => new MarginInlineProperty(),
                PropertyNames.MarginInlineStart,
                PropertyNames.MarginInlineEnd);

            // Logical padding longhands.
            // Logical padding shorthands.
            AddLogicalShorthand(PropertyNames.PaddingBlock, () => new PaddingBlockProperty(),
                PropertyNames.PaddingBlockStart,
                PropertyNames.PaddingBlockEnd);
            AddLogicalShorthand(PropertyNames.PaddingInline, () => new PaddingInlineProperty(),
                PropertyNames.PaddingInlineStart,
                PropertyNames.PaddingInlineEnd);

            // Logical inset longhands.
            // Inset shorthands. `inset` itself (unlike inset-block/inset-inline) is a purely physical
            // shorthand for top/right/bottom/left - CSS Logical Properties 1 §3 defines it in terms of
            // the physical box, not the flow-relative one, despite living in the same spec module.
            AddLogicalShorthand(PropertyNames.Inset, () => new InsetProperty(),
                PropertyNames.Top,
                PropertyNames.Right,
                PropertyNames.Bottom,
                PropertyNames.Left);
            AddLogicalShorthand(PropertyNames.InsetBlock, () => new InsetBlockProperty(),
                PropertyNames.InsetBlockStart,
                PropertyNames.InsetBlockEnd);
            AddLogicalShorthand(PropertyNames.InsetInline, () => new InsetInlineProperty(),
                PropertyNames.InsetInlineStart,
                PropertyNames.InsetInlineEnd);

            // Logical per-edge border longhands.

            // Logical per-edge border shorthands - each its own class (not reused from the physical
            // border-top/bottom/left/right shorthand classes, unlike before this fix): border-top's own
            // shorthand must keep expanding into border-top-width/-style/-color regardless of this box's
            // writing-mode/direction, so it cannot also serve as border-block-start's shorthand once
            // border-block-start expands into its own logical longhands instead.
            AddLogicalShorthand(PropertyNames.BorderBlockStart, () => new BorderBlockStartProperty(),
                PropertyNames.BorderBlockStartWidth,
                PropertyNames.BorderBlockStartStyle,
                PropertyNames.BorderBlockStartColor);
            AddLogicalShorthand(PropertyNames.BorderBlockEnd, () => new BorderBlockEndProperty(),
                PropertyNames.BorderBlockEndWidth,
                PropertyNames.BorderBlockEndStyle,
                PropertyNames.BorderBlockEndColor);
            AddLogicalShorthand(PropertyNames.BorderInlineStart, () => new BorderInlineStartProperty(),
                PropertyNames.BorderInlineStartWidth,
                PropertyNames.BorderInlineStartStyle,
                PropertyNames.BorderInlineStartColor);
            AddLogicalShorthand(PropertyNames.BorderInlineEnd, () => new BorderInlineEndProperty(),
                PropertyNames.BorderInlineEndWidth,
                PropertyNames.BorderInlineEndStyle,
                PropertyNames.BorderInlineEndColor);

            // Logical two-edge border shorthands (both block/inline edges the same value).
            AddLogicalShorthand(PropertyNames.BorderBlock, () => new BorderBlockProperty(),
                PropertyNames.BorderBlockStartWidth,
                PropertyNames.BorderBlockStartStyle,
                PropertyNames.BorderBlockStartColor,
                PropertyNames.BorderBlockEndWidth,
                PropertyNames.BorderBlockEndStyle,
                PropertyNames.BorderBlockEndColor);
            AddLogicalShorthand(PropertyNames.BorderInline, () => new BorderInlineProperty(),
                PropertyNames.BorderInlineStartWidth,
                PropertyNames.BorderInlineStartStyle,
                PropertyNames.BorderInlineStartColor,
                PropertyNames.BorderInlineEndWidth,
                PropertyNames.BorderInlineEndStyle,
                PropertyNames.BorderInlineEndColor);

            // Logical two-edge border width/style/color shorthands.
            AddLogicalShorthand(PropertyNames.BorderBlockWidth, () => new BorderBlockWidthProperty(),
                PropertyNames.BorderBlockStartWidth,
                PropertyNames.BorderBlockEndWidth);
            AddLogicalShorthand(PropertyNames.BorderBlockStyle, () => new BorderBlockStyleProperty(),
                PropertyNames.BorderBlockStartStyle,
                PropertyNames.BorderBlockEndStyle);
            AddLogicalShorthand(PropertyNames.BorderBlockColor, () => new BorderBlockColorProperty(),
                PropertyNames.BorderBlockStartColor,
                PropertyNames.BorderBlockEndColor);
            AddLogicalShorthand(PropertyNames.BorderInlineWidth, () => new BorderInlineWidthProperty(),
                PropertyNames.BorderInlineStartWidth,
                PropertyNames.BorderInlineEndWidth);
            AddLogicalShorthand(PropertyNames.BorderInlineStyle, () => new BorderInlineStyleProperty(),
                PropertyNames.BorderInlineStartStyle,
                PropertyNames.BorderInlineEndStyle);
            AddLogicalShorthand(PropertyNames.BorderInlineColor, () => new BorderInlineColorProperty(),
                PropertyNames.BorderInlineStartColor,
                PropertyNames.BorderInlineEndColor);

            _fontsBuilder.Add(PropertyNames.Src, () => new SrcProperty());
            _fontsBuilder.Add(PropertyNames.UnicodeRange, () => new UnicodeRangeProperty());
            _fontsBuilder.Add(PropertyNames.FontVariant, () => new FontFaceVariantProperty());
            // The descriptors that take ranges (and "auto") are not the properties of the same names.
            _fontsBuilder.Add(PropertyNames.FontStyle, () => new FontFaceDescriptorProperty(PropertyNames.FontStyle));
            _fontsBuilder.Add(PropertyNames.FontWeight, () => new FontFaceDescriptorProperty(PropertyNames.FontWeight));
            _fontsBuilder.Add(PropertyNames.FontStretch, () => new FontFaceDescriptorProperty(PropertyNames.FontStretch));

            _fonts = _fontsBuilder.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

            var propertyDescriptorsBuilder = new Dictionary<string, LonghandCreator>(StringComparer.OrdinalIgnoreCase)
            {
                [PropertyNames.Syntax] = () => new UnknownProperty(PropertyNames.Syntax),
                [PropertyNames.InitialValue] = () => new UnknownProperty(PropertyNames.InitialValue),
                [PropertyNames.Inherits] = () => new UnknownProperty(PropertyNames.Inherits),
            };
            _propertyDescriptors = propertyDescriptorsBuilder.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

            var fontPaletteDescriptorsBuilder = new Dictionary<string, LonghandCreator>(StringComparer.OrdinalIgnoreCase)
            {
                [PropertyNames.FontFamily] = () => new UnknownProperty(PropertyNames.FontFamily),
                [PropertyNames.BasePalette] = () => new UnknownProperty(PropertyNames.BasePalette),
                [PropertyNames.OverrideColors] = () => new UnknownProperty(PropertyNames.OverrideColors),
            };
            _fontPaletteDescriptors = fontPaletteDescriptorsBuilder.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

            // `all` covers every longhand registered above (so a newly added property is included automatically),
            // except those the spec exempts (direction, unicode-bidi) and the non-standard legacy flexbox spellings.
            AddLogicalShorthand(PropertyNames.All, () => new AllProperty(),
                _longhandsBuilder.Keys.Where(name => !IsExcludedFromAll(name)).ToArray());

            // CSS Counter Styles Level 3 §3 descriptors, stored raw (UnknownProperty) and parsed in Layer B
            // (Html/Core/CounterStyles). speak-as is accepted and ignored (no speech output).
            var counterStyleDescriptorsBuilder = new Dictionary<string, LonghandCreator>(StringComparer.OrdinalIgnoreCase);
            foreach (var descriptor in CounterStyleRule.DescriptorNames)
            {
                var descriptorName = descriptor;
                counterStyleDescriptorsBuilder[descriptorName] = () => new UnknownProperty(descriptorName);
            }

            _counterStyleDescriptors = counterStyleDescriptorsBuilder.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

            _longhands = _longhandsBuilder.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
            _mappings = _mappingsBuilder.ToFrozenDictionary();
            _shorthands = _shorthandsBuilder.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        }

        internal static PropertyFactory Instance => Lazy.Value;

        /// <summary>Longhands the <c>all</c> shorthand leaves alone: <c>direction</c> and <c>unicode-bidi</c> (CSS Cascade 4 §3.2) and the legacy <c>-webkit-box-*</c> spellings.</summary>
        internal static bool IsExcludedFromAll(string name) =>
            name.Equals(PropertyNames.Direction, StringComparison.OrdinalIgnoreCase)
            || name.Equals(PropertyNames.UnicodeBidirectional, StringComparison.OrdinalIgnoreCase)
            || LegacyBoxProperty.Prefixes.Any(prefix => new[]
                {
                    LegacyBoxProperty.Orient, LegacyBoxProperty.Direction, LegacyBoxProperty.Pack, LegacyBoxProperty.Align
                }.Any(baseName => name.Equals(prefix + baseName, StringComparison.OrdinalIgnoreCase)));

        private void AddShorthand(string name, ShorthandCreator creator, params string[] longhands)
        {
            _shorthandsBuilder.Add(name, creator);
            _mappingsBuilder.Add(name, longhands);
        }

        // A logical shorthand: registered for parsing/expansion, but excluded from serialization reconstruction
        // (see the _logicalShorthands field comment).
        private void AddLogicalShorthand(string name, ShorthandCreator creator, params string[] longhands)
        {
            AddShorthand(name, creator, longhands);
            _logicalShorthands.Add(name);
        }

        private void AddLonghand(string name, LonghandCreator creator, bool animatable = false, bool font = false)
        {
            _longhandsBuilder.Add(name, creator);

            if (animatable) _animatables.Add(name);

            if (font) _fontsBuilder.Add(name, creator);
        }

        public Property Create(string name)
        {
            name = VendorPropertyAliases.Canonicalize(name);
            return CreateLonghand(name) ?? CreateShorthand(name) ?? CreateCustomProperty(name);
        }

        private static Property CreateCustomProperty(string name)
        {
            return IsCustomPropertyName(name) ? new CustomProperty(name) : null;
        }

        internal static bool IsCustomPropertyName(string name)
        {
            return name is { Length: > 2 } && name[0] == '-' && name[1] == '-';
        }

        public Property CreateFont(string name)
        {
            return _fonts.TryGetValue(name, out var propertyCreator) ? propertyCreator() : null;
        }

        public Property CreatePropertyDescriptor(string name)
        {
            return _propertyDescriptors.TryGetValue(name, out var propertyCreator) ? propertyCreator() : null;
        }

        public Property CreateCounterStyleDescriptor(string name)
        {
            return _counterStyleDescriptors.TryGetValue(name, out var propertyCreator) ? propertyCreator() : null;
        }

        public Property CreateFontPaletteDescriptor(string name)
        {
            return _fontPaletteDescriptors.TryGetValue(name, out var propertyCreator) ? propertyCreator() : null;
        }

        // Unlike the fixed @property/@font-palette-values descriptor sets above, a @font-feature-values
        // nested block's declarations are arbitrary author-chosen feature-value names (e.g. "nice-style:
        // 12;"), not a fixed descriptor vocabulary - so every ident is accepted here, stored raw via
        // UnknownProperty and validated/resolved later in Layer B (RegisteredFontFeatureValues.BuildRegistry).
        public Property CreateFontFeatureValueDescriptor(string name)
        {
            return string.IsNullOrEmpty(name) ? null : new UnknownProperty(name);
        }

        public Property CreateViewport(string name)
        {
            var feature = MediaFeatureFactory.Instance.Create(name);

            return feature != null ? new FeatureProperty(feature) : null;
        }

        public Property CreateLonghand(string name)
        {
            return _longhands.TryGetValue(VendorPropertyAliases.Canonicalize(name), out var createProperty) ? createProperty() : null;
        }

        public ShorthandProperty CreateShorthand(string name)
        {
            return _shorthands.TryGetValue(VendorPropertyAliases.Canonicalize(name), out var propertyCreator) ? propertyCreator() : null;
        }

        public Property[] CreateLonghandsFor(string name)
        {
            var propertyNames = GetLonghands(name);

            return propertyNames.Select(CreateLonghand).ToArray();
        }

        public bool IsShorthand(string name)
        {
            return _shorthands.ContainsKey(VendorPropertyAliases.Canonicalize(name));
        }

        public bool IsAnimatable(string name)
        {
            name = VendorPropertyAliases.Canonicalize(name);
            return _longhands.ContainsKey(name)
                ? _animatables.Contains(name)
                : GetLonghands(name).Any(_ => _animatables.Contains(name));
        }

        public string[] GetLonghands(string name)
        {
            return _mappings.TryGetValue(VendorPropertyAliases.Canonicalize(name), out var mapping)
                ? mapping
                : Array.Empty<string>();
        }

        public IEnumerable<string> GetShorthands(string name)
        {
            return from mapping in _mappings
                   where !_logicalShorthands.Contains(mapping.Key)
                         && mapping.Value.Contains(name, StringComparison.OrdinalIgnoreCase)
                   select mapping.Key;
        }

        private delegate Property LonghandCreator();

        private delegate ShorthandProperty ShorthandCreator();
    }
}
