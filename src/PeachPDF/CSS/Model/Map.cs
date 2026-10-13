using System;
using System.Collections.Generic;

namespace PeachPDF.CSS
{
    internal static class Map
    {
        public static readonly KeywordMap<Whitespace> WhitespaceModes =
            new KeywordMap<Whitespace>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.Normal, Whitespace.Normal},
                {Keywords.Pre, Whitespace.Pre},
                {Keywords.Nowrap, Whitespace.NoWrap},
                {Keywords.PreWrap, Whitespace.PreWrap},
                {Keywords.PreLine, Whitespace.PreLine}
            }.Freeze();
        public static readonly KeywordMap<TextOverflow> TextOverflowModes =
            new KeywordMap<TextOverflow>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.Clip, TextOverflow.Clip},
                {Keywords.Ellipsis, TextOverflow.Ellipsis}
            }.Freeze();
        public static readonly KeywordMap<TextTransform> TextTransforms =
            new KeywordMap<TextTransform>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.None, TextTransform.None},
                {Keywords.Capitalize, TextTransform.Capitalize},
                {Keywords.Uppercase, TextTransform.Uppercase},
                {Keywords.Lowercase, TextTransform.Lowercase},
                {Keywords.FullWidth, TextTransform.FullWidth}
            }.Freeze();
        public static readonly KeywordMap<TextAlignLast> TextAlignmentsLast =
            new KeywordMap<TextAlignLast>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.Auto, TextAlignLast.Auto},
                {Keywords.Start, TextAlignLast.Start},
                {Keywords.End, TextAlignLast.End},
                {Keywords.Right, TextAlignLast.Right},
                {Keywords.Left, TextAlignLast.Left},
                {Keywords.Center, TextAlignLast.Center},
                {Keywords.Justify, TextAlignLast.Justify},
                {Keywords.MatchParent, TextAlignLast.MatchParent}
            }.Freeze();
        public static readonly KeywordMap<TextAnchor> TextAnchors =
            new KeywordMap<TextAnchor>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.Start, TextAnchor.Start},
                {Keywords.Middle, TextAnchor.Middle},
                {Keywords.End, TextAnchor.End}
            }.Freeze();
        public static readonly KeywordMap<TextJustify> TextJustifyOptions =
            new KeywordMap<TextJustify>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.Auto, TextJustify.Auto},
                {Keywords.Distribute, TextJustify.Distribute},
                {Keywords.DistributeAllLines, TextJustify.DistributeAllLines},
                {Keywords.DistributeCenterLast, TextJustify.DistributeCenterLast},
                {Keywords.InterCluster, TextJustify.InterCluster},
                {Keywords.InterIdeograph, TextJustify.InterIdeograph},
                {Keywords.InterWord, TextJustify.InterWord},
                {Keywords.Kashida, TextJustify.Kashida},
                {Keywords.Newspaper, TextJustify.Newspaper}
            }.Freeze();
        public static readonly KeywordMap<JustifyContent> JustifyContentOptions =
            new KeywordMap<JustifyContent>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.Start, JustifyContent.Start},
                {Keywords.Center, JustifyContent.Center},
                {Keywords.End, JustifyContent.End},
                {Keywords.FlexStart, JustifyContent.FlexStart},
                {Keywords.FlexEnd, JustifyContent.FlexEnd},
                {Keywords.Left, JustifyContent.Left},
                {Keywords.Right, JustifyContent.Right},
                {Keywords.Normal, JustifyContent.Normal },
                {Keywords.SpaceBetween, JustifyContent.SpaceBetween},
                {Keywords.SpaceAround, JustifyContent.SpaceAround},
                {Keywords.SpaceEvenly, JustifyContent.SpaceEvenly},
                {Keywords.Stretch, JustifyContent.Stretch },
            }.Freeze();
        public static readonly KeywordMap<HorizontalAlignment> HorizontalAlignments =
            new KeywordMap<HorizontalAlignment>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.Left, HorizontalAlignment.Left},
                {Keywords.Right, HorizontalAlignment.Right},
                {Keywords.Center, HorizontalAlignment.Center},
                {Keywords.Justify, HorizontalAlignment.Justify},
                {Keywords.Start, HorizontalAlignment.Start},
                {Keywords.End, HorizontalAlignment.End},
                {Keywords.MatchParent, HorizontalAlignment.MatchParent}
            }.Freeze();
        public static readonly KeywordMap<TextDecorationThicknessKeyword> TextDecorationThicknessKeywords =
            new KeywordMap<TextDecorationThicknessKeyword>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.Auto, TextDecorationThicknessKeyword.Auto},
                {Keywords.FromFont, TextDecorationThicknessKeyword.FromFont}
            }.Freeze();
        public static readonly KeywordMap<TextDecorationSkipInk> TextDecorationSkipInkModes =
            new KeywordMap<TextDecorationSkipInk>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.Auto, TextDecorationSkipInk.Auto},
                {Keywords.None, TextDecorationSkipInk.None},
                {Keywords.All, TextDecorationSkipInk.All}
            }.Freeze();
        public static readonly KeywordMap<TextUnderlineOffsetKeyword> TextUnderlineOffsetKeywords =
            new KeywordMap<TextUnderlineOffsetKeyword>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.Auto, TextUnderlineOffsetKeyword.Auto}
            }.Freeze();
        public static readonly KeywordMap<TextUnderlinePosition> TextUnderlinePositions =
            new KeywordMap<TextUnderlinePosition>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.Auto, TextUnderlinePosition.Auto},
                {Keywords.FromFont, TextUnderlinePosition.FromFont},
                {Keywords.Under, TextUnderlinePosition.Under}
            }.Freeze();
        public static readonly KeywordMap<TextUnderlineSide> TextUnderlineSides =
            new KeywordMap<TextUnderlineSide>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.Left, TextUnderlineSide.Left},
                {Keywords.Right, TextUnderlineSide.Right}
            }.Freeze();
        public static readonly KeywordMap<VerticalAlignment> VerticalAlignments =
            new KeywordMap<VerticalAlignment>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.Baseline, VerticalAlignment.Baseline},
                {Keywords.Sub, VerticalAlignment.Sub},
                {Keywords.Super, VerticalAlignment.Super},
                {Keywords.TextTop, VerticalAlignment.TextTop},
                {Keywords.TextBottom, VerticalAlignment.TextBottom},
                {Keywords.Middle, VerticalAlignment.Middle},
                {Keywords.Top, VerticalAlignment.Top},
                {Keywords.Bottom, VerticalAlignment.Bottom}
            }.Freeze();
        public static readonly KeywordMap<LineStyle> LineStyles =
            new KeywordMap<LineStyle>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.None, LineStyle.None},
                {Keywords.Solid, LineStyle.Solid},
                {Keywords.Double, LineStyle.Double},
                {Keywords.Dotted, LineStyle.Dotted},
                {Keywords.Dashed, LineStyle.Dashed},
                {Keywords.Inset, LineStyle.Inset},
                {Keywords.Outset, LineStyle.Outset},
                {Keywords.Ridge, LineStyle.Ridge},
                {Keywords.Groove, LineStyle.Groove},
                {Keywords.Hidden, LineStyle.Hidden}
            }.Freeze();
        public static readonly KeywordMap<OutlineStyle> OutlineStyles =
            new KeywordMap<OutlineStyle>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.None, OutlineStyle.None},
                {Keywords.Solid, OutlineStyle.Solid},
                {Keywords.Double, OutlineStyle.Double},
                {Keywords.Dotted, OutlineStyle.Dotted},
                {Keywords.Dashed, OutlineStyle.Dashed},
                {Keywords.Inset, OutlineStyle.Inset},
                {Keywords.Outset, OutlineStyle.Outset},
                {Keywords.Ridge, OutlineStyle.Ridge},
                {Keywords.Groove, OutlineStyle.Groove},
                // No `hidden`: css-ui-4 §3.3 defines outline-style as `auto | <'border-style'>` *except*
                // `hidden`, so the keyword is invalid here even though it is valid for every
                // border-*-style. Leaving it out is what makes an authored `outline-style: hidden`
                // fail Layer A validation and have its declaration dropped, so an earlier
                // `outline: 2px solid red` in the same rule keeps painting - which is the whole point.
                // Mapping it instead made the declaration win and then paint nothing.
                {Keywords.Auto, OutlineStyle.Auto}
            }.Freeze();
        public static readonly KeywordMap<PdfTagType> PdfTagTypes =
            new KeywordMap<PdfTagType>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.Auto, PdfTagType.Auto},
                {Keywords.None, PdfTagType.None},
                {Keywords.Part, PdfTagType.Part},
                {Keywords.Art, PdfTagType.Art},
                {Keywords.Sect, PdfTagType.Sect},
                {Keywords.Div, PdfTagType.Div},
                {Keywords.Index, PdfTagType.Index},
                {Keywords.BlockQuote, PdfTagType.BlockQuote},
                {Keywords.Caption, PdfTagType.Caption},
                {Keywords.Toc, PdfTagType.Toc},
                {Keywords.Toci, PdfTagType.Toci},
                {Keywords.P, PdfTagType.P},
                {Keywords.H1, PdfTagType.H1},
                {Keywords.H2, PdfTagType.H2},
                {Keywords.H3, PdfTagType.H3},
                {Keywords.H4, PdfTagType.H4},
                {Keywords.H5, PdfTagType.H5},
                {Keywords.H6, PdfTagType.H6},
                {Keywords.L, PdfTagType.L},
                {Keywords.Li, PdfTagType.Li},
                {Keywords.Lbl, PdfTagType.Lbl},
                {Keywords.LBody, PdfTagType.LBody},
                {Keywords.Dl, PdfTagType.Dl},
                {Keywords.DlDiv, PdfTagType.DlDiv},
                {Keywords.Dt, PdfTagType.Dt},
                {Keywords.Dd, PdfTagType.Dd},
                {Keywords.Span, PdfTagType.Span},
                {Keywords.Quote, PdfTagType.Quote},
                {Keywords.Table, PdfTagType.Table},
                {Keywords.Tr, PdfTagType.Tr},
                {Keywords.Th, PdfTagType.Th},
                {Keywords.Td, PdfTagType.Td},
                {Keywords.THead, PdfTagType.THead},
                {Keywords.TBody, PdfTagType.TBody},
                {Keywords.TFoot, PdfTagType.TFoot},
                {Keywords.BibEntry, PdfTagType.BibEntry},
                {Keywords.Code, PdfTagType.Code},
                {Keywords.Figure, PdfTagType.Figure},
                {Keywords.Formula, PdfTagType.Formula},
                {Keywords.Artifact, PdfTagType.Artifact},
                {Keywords.Note, PdfTagType.Note},
                {Keywords.Reference, PdfTagType.Reference},
                {Keywords.Link, PdfTagType.Link}
            }.Freeze();
        public static readonly KeywordMap<PdfFormFieldKeyword> PdfFormFieldKinds =
            new KeywordMap<PdfFormFieldKeyword>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.Auto, PdfFormFieldKeyword.Auto},
                {Keywords.None, PdfFormFieldKeyword.None},
                {Keywords.Text, PdfFormFieldKeyword.Text},
                {Keywords.Checkbox, PdfFormFieldKeyword.Checkbox},
                {Keywords.Radio, PdfFormFieldKeyword.Radio},
                {Keywords.Select, PdfFormFieldKeyword.Select}
            }.Freeze();
        public static readonly KeywordMap<BoxModel> BoxModels =
            new KeywordMap<BoxModel>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.BorderBox, BoxModel.BorderBox},
                {Keywords.PaddingBox, BoxModel.PaddingBox},
                {Keywords.ContentBox, BoxModel.ContentBox}
            }.Freeze();
        // background-clip's own grammar - BoxModels' three values plus "text" (issue #1117). Kept
        // separate from BoxModels/BoxModel so background-origin/box-sizing (which share those) never
        // accept "text".
        public static readonly KeywordMap<BackgroundClipKeyword> BackgroundClips =
            new KeywordMap<BackgroundClipKeyword>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.BorderBox, BackgroundClipKeyword.BorderBox},
                {Keywords.PaddingBox, BackgroundClipKeyword.PaddingBox},
                {Keywords.ContentBox, BackgroundClipKeyword.ContentBox},
                {Keywords.Text, BackgroundClipKeyword.Text}
            }.Freeze();
        public static readonly KeywordMap<ITimingFunction> TimingFunctions =
            new KeywordMap<ITimingFunction>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.Ease, new CubicBezierTimingFunction(0.25f, 0.1f, 0.25f, 1f)},
                {Keywords.EaseIn, new CubicBezierTimingFunction(0.42f, 0f, 1f, 1f)},
                {Keywords.EaseOut, new CubicBezierTimingFunction(0f, 0f, 0.58f, 1f)},
                {Keywords.EaseInOut, new CubicBezierTimingFunction(0.42f, 0f, 0.58f, 1f)},
                {Keywords.Linear, new CubicBezierTimingFunction(0f, 0f, 1f, 1f)},
                {Keywords.StepStart, new StepsTimingFunction(1, true)},
                {Keywords.StepEnd, new StepsTimingFunction(1)}
            }.Freeze();
        public static readonly KeywordMap<AnimationFillStyle> AnimationFillStyles =
            new KeywordMap<AnimationFillStyle>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.None, AnimationFillStyle.None},
                {Keywords.Forwards, AnimationFillStyle.Forwards},
                {Keywords.Backwards, AnimationFillStyle.Backwards},
                {Keywords.Both, AnimationFillStyle.Both}
            }.Freeze();
        public static readonly KeywordMap<AnimationDirection> AnimationDirections =
            new KeywordMap<AnimationDirection>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.Normal, AnimationDirection.Normal},
                {Keywords.Reverse, AnimationDirection.Reverse},
                {Keywords.Alternate, AnimationDirection.Alternate},
                {Keywords.AlternateReverse, AnimationDirection.AlternateReverse}
            }.Freeze();
        public static readonly KeywordMap<Visibility> Visibilities =
            new KeywordMap<Visibility>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.Visible, Visibility.Visible},
                {Keywords.Hidden, Visibility.Hidden},
                {Keywords.Collapse, Visibility.Collapse}
            }.Freeze();
        public static readonly KeywordMap<Hyphens> HyphensModes =
            new KeywordMap<Hyphens>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.None, Hyphens.None},
                {Keywords.Manual, Hyphens.Manual},
                {Keywords.Auto, Hyphens.Auto}
            }.Freeze();
        public static readonly KeywordMap<HyphenateLimitLast> HyphenateLimitLastModes =
            new KeywordMap<HyphenateLimitLast>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.None, HyphenateLimitLast.None},
                {Keywords.Always, HyphenateLimitLast.Always},
                {Keywords.Column, HyphenateLimitLast.Column},
                {Keywords.Page, HyphenateLimitLast.Page},
                {Keywords.Spread, HyphenateLimitLast.Spread}
            }.Freeze();
        public static readonly KeywordMap<NoLimitKeyword> NoLimitKeywords =
            new KeywordMap<NoLimitKeyword>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.NoLimit, NoLimitKeyword.NoLimit}
            }.Freeze();
        public static readonly KeywordMap<PlayState> PlayStates =
            new KeywordMap<PlayState>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.Running, PlayState.Running},
                {Keywords.Paused, PlayState.Paused}
            }.Freeze();
        // The full 7-keyword font-variant-caps grammar - the real longhand, and reachable from
        // standalone `font-variant`. Named *Keywords (plural), not FontVariantCaps, to avoid colliding
        // with the FontVariantCaps enum type itself - same convention as FontVariantLigatureTokens
        // (dict) vs. FontVariantLigatureToken (enum) below.
        public static readonly KeywordMap<FontVariantCaps> FontVariantCapsKeywords =
            new KeywordMap<FontVariantCaps>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.Normal, FontVariantCaps.Normal},
                {Keywords.SmallCaps, FontVariantCaps.SmallCaps},
                {Keywords.AllSmallCaps, FontVariantCaps.AllSmallCaps},
                {Keywords.PetiteCaps, FontVariantCaps.PetiteCaps},
                {Keywords.AllPetiteCaps, FontVariantCaps.AllPetiteCaps},
                {Keywords.Unicase, FontVariantCaps.Unicase},
                {Keywords.TitlingCaps, FontVariantCaps.TitlingCaps}
            }.Freeze();
        // Named *Keywords (plural) rather than FontVariantPosition, to avoid colliding with the
        // FontVariantPosition enum type itself - same convention as FontVariantCapsKeywords above.
        public static readonly KeywordMap<FontVariantPosition> FontVariantPositionKeywords =
            new KeywordMap<FontVariantPosition>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.Normal, FontVariantPosition.Normal},
                {Keywords.Sub, FontVariantPosition.Sub},
                {Keywords.Super, FontVariantPosition.Super}
            }.Freeze();
        public static readonly KeywordMap<FontVariantEmoji> FontVariantEmojiKeywords =
            new KeywordMap<FontVariantEmoji>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.Normal, FontVariantEmoji.Normal},
                {Keywords.Text, FontVariantEmoji.Text},
                {Keywords.Emoji, FontVariantEmoji.Emoji},
                {Keywords.Unicode, FontVariantEmoji.Unicode}
            }.Freeze();
        // The restricted `<font-variant-css2>` grammar (normal | small-caps) - the only caps values
        // reachable from inside the `font` shorthand's embedded variant slot, per spec.
        public static readonly KeywordMap<FontVariantCaps> FontVariantCss2Keywords =
            new KeywordMap<FontVariantCaps>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.Normal, FontVariantCaps.Normal},
                {Keywords.SmallCaps, FontVariantCaps.SmallCaps}
            }.Freeze();
        public static readonly KeywordMap<FontVariantLigatureToken> FontVariantLigatureTokens =
            new KeywordMap<FontVariantLigatureToken>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.CommonLigatures, FontVariantLigatureToken.CommonLigatures},
                {Keywords.NoCommonLigatures, FontVariantLigatureToken.NoCommonLigatures},
                {Keywords.DiscretionaryLigatures, FontVariantLigatureToken.DiscretionaryLigatures},
                {Keywords.NoDiscretionaryLigatures, FontVariantLigatureToken.NoDiscretionaryLigatures},
                {Keywords.HistoricalLigatures, FontVariantLigatureToken.HistoricalLigatures},
                {Keywords.NoHistoricalLigatures, FontVariantLigatureToken.NoHistoricalLigatures},
                {Keywords.Contextual, FontVariantLigatureToken.Contextual},
                {Keywords.NoContextual, FontVariantLigatureToken.NoContextual}
            }.Freeze();
        public static readonly KeywordMap<FontVariantNumericToken> FontVariantNumericTokens =
            new KeywordMap<FontVariantNumericToken>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.LiningNums, FontVariantNumericToken.LiningNums},
                {Keywords.OldstyleNums, FontVariantNumericToken.OldstyleNums},
                {Keywords.ProportionalNums, FontVariantNumericToken.ProportionalNums},
                {Keywords.TabularNums, FontVariantNumericToken.TabularNums},
                {Keywords.DiagonalFractions, FontVariantNumericToken.DiagonalFractions},
                {Keywords.StackedFractions, FontVariantNumericToken.StackedFractions},
                {Keywords.Ordinal, FontVariantNumericToken.Ordinal},
                {Keywords.SlashedZero, FontVariantNumericToken.SlashedZero}
            }.Freeze();
        public static readonly KeywordMap<FontVariantEastAsianToken> FontVariantEastAsianTokens =
            new KeywordMap<FontVariantEastAsianToken>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.Jis78Forms, FontVariantEastAsianToken.Jis78Forms},
                {Keywords.Jis83Forms, FontVariantEastAsianToken.Jis83Forms},
                {Keywords.Jis90Forms, FontVariantEastAsianToken.Jis90Forms},
                {Keywords.Jis04Forms, FontVariantEastAsianToken.Jis04Forms},
                {Keywords.Simplified, FontVariantEastAsianToken.Simplified},
                {Keywords.Traditional, FontVariantEastAsianToken.Traditional},
                {Keywords.FullWidth, FontVariantEastAsianToken.FullWidth},
                {Keywords.ProportionalWidth, FontVariantEastAsianToken.ProportionalWidth},
                {Keywords.Ruby, FontVariantEastAsianToken.Ruby}
            }.Freeze();
        public static readonly KeywordMap<DirectionMode> DirectionModes =
            new KeywordMap<DirectionMode>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.Ltr, DirectionMode.Ltr},
                {Keywords.Rtl, DirectionMode.Rtl}
            }.Freeze();
        public static readonly KeywordMap<FontSynthesisMode> FontSynthesisModes =
            new KeywordMap<FontSynthesisMode>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.Auto, FontSynthesisMode.Auto},
                {Keywords.None, FontSynthesisMode.None}
            }.Freeze();
        public static readonly KeywordMap<TextRenderingMode> TextRenderingModes =
            new KeywordMap<TextRenderingMode>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.Auto, TextRenderingMode.Auto},
                {"optimizeSpeed", TextRenderingMode.OptimizeSpeed},
                {"optimizeLegibility", TextRenderingMode.OptimizeLegibility},
                {"geometricPrecision", TextRenderingMode.GeometricPrecision}
            }.Freeze();
        public static readonly KeywordMap<FontKerningMode> FontKerningModes =
            new KeywordMap<FontKerningMode>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.Auto, FontKerningMode.Auto},
                {Keywords.Normal, FontKerningMode.Normal},
                {Keywords.None, FontKerningMode.None}
            }.Freeze();
        public static readonly KeywordMap<FontOpticalSizingMode> FontOpticalSizingModes =
            new KeywordMap<FontOpticalSizingMode>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.Auto, FontOpticalSizingMode.Auto},
                {Keywords.None, FontOpticalSizingMode.None}
            }.Freeze();
        public static readonly KeywordMap<AutoKeyword> AutoKeywords =
            new KeywordMap<AutoKeyword>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.Auto, AutoKeyword.Auto}
            }.Freeze();
        public static readonly KeywordMap<NoneKeyword> NoneKeywords =
            new KeywordMap<NoneKeyword>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.None, NoneKeyword.None}
            }.Freeze();
        public static readonly KeywordMap<BookmarkState> BookmarkStates =
            new KeywordMap<BookmarkState>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.Open, BookmarkState.Open},
                {Keywords.Closed, BookmarkState.Closed}
            }.Freeze();
        public static readonly KeywordMap<NormalKeyword> NormalKeywords =
            new KeywordMap<NormalKeyword>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.Normal, NormalKeyword.Normal}
            }.Freeze();
        public static readonly KeywordMap<FontWeightKeyword> FontWeightKeywords =
            new KeywordMap<FontWeightKeyword>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.Normal, FontWeightKeyword.Normal},
                {Keywords.Bold, FontWeightKeyword.Bold},
                {Keywords.Bolder, FontWeightKeyword.Bolder},
                {Keywords.Lighter, FontWeightKeyword.Lighter}
            }.Freeze();
        public static readonly KeywordMap<FontSizeKeyword> FontSizeKeywords =
            new KeywordMap<FontSizeKeyword>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.XxSmall, FontSizeKeyword.XxSmall},
                {Keywords.XSmall, FontSizeKeyword.XSmall},
                {Keywords.Small, FontSizeKeyword.Small},
                {Keywords.Medium, FontSizeKeyword.Medium},
                {Keywords.Large, FontSizeKeyword.Large},
                {Keywords.XLarge, FontSizeKeyword.XLarge},
                {Keywords.XxLarge, FontSizeKeyword.XxLarge},
                {Keywords.Smaller, FontSizeKeyword.Smaller},
                {Keywords.Larger, FontSizeKeyword.Larger}
            }.Freeze();
        public static readonly KeywordMap<FlexBasisKeyword> FlexBasisKeywords =
            new KeywordMap<FlexBasisKeyword>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.Auto, FlexBasisKeyword.Auto},
                {Keywords.Content, FlexBasisKeyword.Content},
                {Keywords.Stretch, FlexBasisKeyword.Stretch},
                {"-webkit-fill-available", FlexBasisKeyword.Stretch},
                {"-moz-available", FlexBasisKeyword.Stretch}
            }.Freeze();
        public static readonly KeywordMap<BoxSizingMode> BoxSizingModes =
            new KeywordMap<BoxSizingMode>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.BorderBox, BoxSizingMode.BorderBox},
                {Keywords.ContentBox, BoxSizingMode.ContentBox}
            }.Freeze();
        public static readonly KeywordMap<BoxDecorationBreakMode> BoxDecorationBreakModes =
            new KeywordMap<BoxDecorationBreakMode>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.Slice, BoxDecorationBreakMode.Slice},
                {Keywords.Clone, BoxDecorationBreakMode.Clone}
            }.Freeze();
        public static readonly KeywordMap<ColumnFillMode> ColumnFillModes =
            new KeywordMap<ColumnFillMode>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.Auto, ColumnFillMode.Auto},
                {Keywords.Balance, ColumnFillMode.Balance}
            }.Freeze();
        public static readonly KeywordMap<ColumnSpanMode> ColumnSpanModes =
            new KeywordMap<ColumnSpanMode>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.None, ColumnSpanMode.None},
                {Keywords.All, ColumnSpanMode.All}
            }.Freeze();
        public static readonly KeywordMap<TransformStyleMode> TransformStyleModes =
            new KeywordMap<TransformStyleMode>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.Flat, TransformStyleMode.Flat},
                {Keywords.Preserve3d, TransformStyleMode.Preserve3d}
            }.Freeze();
        public static readonly KeywordMap<BackfaceVisibilityMode> BackfaceVisibilityModes =
            new KeywordMap<BackfaceVisibilityMode>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.Visible, BackfaceVisibilityMode.Visible},
                {Keywords.Hidden, BackfaceVisibilityMode.Hidden}
            }.Freeze();
        public static readonly KeywordMap<BorderCollapseMode> BorderCollapseModes =
            new KeywordMap<BorderCollapseMode>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.Separate, BorderCollapseMode.Separate},
                {Keywords.Collapse, BorderCollapseMode.Collapse}
            }.Freeze();
        public static readonly KeywordMap<EmptyCellsMode> EmptyCellsModes =
            new KeywordMap<EmptyCellsMode>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.Show, EmptyCellsMode.Show},
                {Keywords.Hide, EmptyCellsMode.Hide}
            }.Freeze();
        public static readonly KeywordMap<CaptionSideMode> CaptionSideModes =
            new KeywordMap<CaptionSideMode>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.Top, CaptionSideMode.Top},
                {Keywords.Bottom, CaptionSideMode.Bottom}
            }.Freeze();
        public static readonly KeywordMap<TableLayoutMode> TableLayoutModes =
            new KeywordMap<TableLayoutMode>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.Auto, TableLayoutMode.Auto},
                {Keywords.Fixed, TableLayoutMode.Fixed}
            }.Freeze();
        public static readonly KeywordMap<ListStylePositionMode> ListStylePositionModes =
            new KeywordMap<ListStylePositionMode>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.Outside, ListStylePositionMode.Outside},
                {Keywords.Inside, ListStylePositionMode.Inside}
            }.Freeze();
        public static readonly KeywordMap<TextDecorationStyleMode> TextDecorationStyleModes =
            new KeywordMap<TextDecorationStyleMode>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.Solid, TextDecorationStyleMode.Solid},
                {Keywords.Double, TextDecorationStyleMode.Double},
                {Keywords.Dotted, TextDecorationStyleMode.Dotted},
                {Keywords.Dashed, TextDecorationStyleMode.Dashed},
                {Keywords.Wavy, TextDecorationStyleMode.Wavy}
            }.Freeze();
        public static readonly KeywordMap<FontVariantPositionMode> FontVariantPositionModes =
            new KeywordMap<FontVariantPositionMode>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.Normal, FontVariantPositionMode.Normal},
                {Keywords.Sub, FontVariantPositionMode.Sub},
                {Keywords.Super, FontVariantPositionMode.Super}
            }.Freeze();
        public static readonly KeywordMap<FontVariantEmojiMode> FontVariantEmojiModes =
            new KeywordMap<FontVariantEmojiMode>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.Normal, FontVariantEmojiMode.Normal},
                {Keywords.Text, FontVariantEmojiMode.Text},
                {Keywords.Emoji, FontVariantEmojiMode.Emoji},
                {Keywords.Unicode, FontVariantEmojiMode.Unicode}
            }.Freeze();
        public static readonly KeywordMap<FontVariantCapsMode> FontVariantCapsModes =
            new KeywordMap<FontVariantCapsMode>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.Normal, FontVariantCapsMode.Normal},
                {Keywords.SmallCaps, FontVariantCapsMode.SmallCaps},
                {Keywords.AllSmallCaps, FontVariantCapsMode.AllSmallCaps},
                {Keywords.PetiteCaps, FontVariantCapsMode.PetiteCaps},
                {Keywords.AllPetiteCaps, FontVariantCapsMode.AllPetiteCaps},
                {Keywords.Unicase, FontVariantCapsMode.Unicase},
                {Keywords.TitlingCaps, FontVariantCapsMode.TitlingCaps}
            }.Freeze();
        public static readonly KeywordMap<ObjectFitMode> ObjectFitModes =
            new KeywordMap<ObjectFitMode>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.Fill, ObjectFitMode.Fill},
                {Keywords.Contain, ObjectFitMode.Contain},
                {Keywords.Cover, ObjectFitMode.Cover},
                {Keywords.None, ObjectFitMode.None},
                {Keywords.ScaleDown, ObjectFitMode.ScaleDown}
            }.Freeze();
        public static readonly KeywordMap<WritingMode> WritingModes =
            new KeywordMap<WritingMode>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.HorizontalTb, WritingMode.HorizontalTb},
                {Keywords.VerticalRl, WritingMode.VerticalRl},
                {Keywords.VerticalLr, WritingMode.VerticalLr},
                {Keywords.SidewaysRl, WritingMode.SidewaysRl},
                {Keywords.SidewaysLr, WritingMode.SidewaysLr}
            }.Freeze();
        public static readonly KeywordMap<TextOrientation> TextOrientations =
            new KeywordMap<TextOrientation>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.Mixed, TextOrientation.Mixed},
                {Keywords.Upright, TextOrientation.Upright},
                {Keywords.Sideways, TextOrientation.Sideways}
            }.Freeze();
        public static readonly KeywordMap<ListStyle> ListStyles =
            new KeywordMap<ListStyle>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.Disc, ListStyle.Disc},
                {Keywords.Circle, ListStyle.Circle},
                {Keywords.Square, ListStyle.Square},
                {Keywords.Decimal, ListStyle.Decimal},
                {Keywords.DecimalLeadingZero, ListStyle.DecimalLeadingZero},
                {Keywords.LowerRoman, ListStyle.LowerRoman},
                {Keywords.UpperRoman, ListStyle.UpperRoman},
                {Keywords.LowerGreek, ListStyle.LowerGreek},
                {Keywords.LowerLatin, ListStyle.LowerLatin},
                {Keywords.UpperLatin, ListStyle.UpperLatin},
                {Keywords.Armenian, ListStyle.Armenian},
                {Keywords.UpperArmenian, ListStyle.Armenian},
                {Keywords.LowerArmenian, ListStyle.LowerArmenian},
                {Keywords.Georgian, ListStyle.Georgian},
                {Keywords.LowerAlpha, ListStyle.LowerLatin},
                {Keywords.UpperAlpha, ListStyle.UpperLatin},
                {Keywords.Hebrew, ListStyle.Hebrew},
                {Keywords.Hiragana, ListStyle.Hiragana},
                {Keywords.HiraganaIroha, ListStyle.HiraganaIroha},
                {Keywords.Katakana, ListStyle.Katakana},
                {Keywords.KatakanaIroha, ListStyle.KatakanaIroha},
                {Keywords.ArabicIndic, ListStyle.ArabicIndic},
                {Keywords.Bengali, ListStyle.Bengali},
                {Keywords.Cambodian, ListStyle.Cambodian},
                {Keywords.Khmer, ListStyle.Cambodian},
                {Keywords.CjkDecimal, ListStyle.CjkDecimal},
                {Keywords.CjkEarthlyBranch, ListStyle.CjkEarthlyBranch},
                {Keywords.CjkHeavenlyStem, ListStyle.CjkHeavenlyStem},
                {Keywords.Devanagari, ListStyle.Devanagari},
                {Keywords.DisclosureOpen, ListStyle.DisclosureOpen},
                {Keywords.DisclosureClosed, ListStyle.DisclosureClosed},
                {Keywords.EthiopicNumeric, ListStyle.EthiopicNumeric},
                {Keywords.Gujarati, ListStyle.Gujarati},
                {Keywords.Gurmukhi, ListStyle.Gurmukhi},
                {Keywords.Kannada, ListStyle.Kannada},
                {Keywords.Lao, ListStyle.Lao},
                {Keywords.Malayalam, ListStyle.Malayalam},
                {Keywords.Mongolian, ListStyle.Mongolian},
                {Keywords.Myanmar, ListStyle.Myanmar},
                {Keywords.Oriya, ListStyle.Oriya},
                {Keywords.Persian, ListStyle.Persian},
                {Keywords.Tamil, ListStyle.Tamil},
                {Keywords.Telugu, ListStyle.Telugu},
                {Keywords.Thai, ListStyle.Thai},
                {Keywords.Tibetan, ListStyle.Tibetan},
                {Keywords.None, ListStyle.None}
            }.Freeze();
        public static readonly KeywordMap<ListPosition> ListPositions =
            new KeywordMap<ListPosition>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.Inside, ListPosition.Inside},
                {Keywords.Outside, ListPosition.Outside}
            }.Freeze();
        public static readonly KeywordMap<FontSize> FontSizes =
            new KeywordMap<FontSize>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.XxSmall, FontSize.Tiny},
                {Keywords.XSmall, FontSize.Little},
                {Keywords.Small, FontSize.Small},
                {Keywords.Medium, FontSize.Medium},
                {Keywords.Large, FontSize.Large},
                {Keywords.XLarge, FontSize.Big},
                {Keywords.XxLarge, FontSize.Huge},
                {Keywords.Larger, FontSize.Larger},
                {Keywords.Smaller, FontSize.Smaller}
            }.Freeze();
        public static readonly KeywordMap<TextDecorationStyle> TextDecorationStyles =
            new KeywordMap<TextDecorationStyle>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.Solid, TextDecorationStyle.Solid},
                {Keywords.Double, TextDecorationStyle.Double},
                {Keywords.Dotted, TextDecorationStyle.Dotted},
                {Keywords.Dashed, TextDecorationStyle.Dashed},
                {Keywords.Wavy, TextDecorationStyle.Wavy}
            }.Freeze();
        public static readonly KeywordMap<TextDecorationLine> TextDecorationLines =
            new KeywordMap<TextDecorationLine>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.Underline, TextDecorationLine.Underline},
                {Keywords.Overline, TextDecorationLine.Overline},
                {Keywords.LineThrough, TextDecorationLine.LineThrough},
                {Keywords.Blink, TextDecorationLine.Blink}
            }.Freeze();
        public static readonly KeywordMap<BorderRepeat> BorderRepeatModes =
            new KeywordMap<BorderRepeat>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.Stretch, BorderRepeat.Stretch},
                {Keywords.Repeat, BorderRepeat.Repeat},
                {Keywords.Round, BorderRepeat.Round},
                {Keywords.Space, BorderRepeat.Space}
            }.Freeze();
        public static readonly KeywordMap<string> DefaultFontFamilies =
            new KeywordMap<string>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.Serif, "Times New Roman"},
                {Keywords.SansSerif, "Arial"},
                {Keywords.Monospace, "Consolas"},
                {Keywords.Cursive, "Cursive"},
                {Keywords.Fantasy, "Comic Sans"},
                {Keywords.Math, "Cambria Math"}
            }.Freeze();
        public static readonly KeywordMap<BackgroundAttachment> BackgroundAttachments =
            new KeywordMap<BackgroundAttachment>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.Fixed, BackgroundAttachment.Fixed},
                {Keywords.Local, BackgroundAttachment.Local},
                {Keywords.Scroll, BackgroundAttachment.Scroll}
            }.Freeze();
        public static readonly KeywordMap<FontStyle> FontStyles =
            new KeywordMap<FontStyle>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.Normal, FontStyle.Normal},
                {Keywords.Italic, FontStyle.Italic},
                {Keywords.Oblique, FontStyle.Oblique}
            }.Freeze();
        public static readonly KeywordMap<FontStretch> FontStretches =
            new KeywordMap<FontStretch>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.Normal, FontStretch.Normal},
                {Keywords.UltraCondensed, FontStretch.UltraCondensed},
                {Keywords.ExtraCondensed, FontStretch.ExtraCondensed},
                {Keywords.Condensed, FontStretch.Condensed},
                {Keywords.SemiCondensed, FontStretch.SemiCondensed},
                {Keywords.SemiExpanded, FontStretch.SemiExpanded},
                {Keywords.Expanded, FontStretch.Expanded},
                {Keywords.ExtraExpanded, FontStretch.ExtraExpanded},
                {Keywords.UltraExpanded, FontStretch.UltraExpanded}
            }.Freeze();
        /// <summary>css-break-3 §3.1 break-before/break-after. Note "always" is not one of them - it is a legacy page-break-* value only.</summary>
        public static readonly KeywordMap<BreakMode> BreakModes =
            new KeywordMap<BreakMode>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.Auto, BreakMode.Auto},
                {Keywords.Avoid, BreakMode.Avoid},
                {Keywords.AvoidPage, BreakMode.AvoidPage},
                {Keywords.Page, BreakMode.Page},
                {Keywords.Left, BreakMode.Left},
                {Keywords.Right, BreakMode.Right},
                {Keywords.Recto, BreakMode.Recto},
                {Keywords.Verso, BreakMode.Verso},
                {Keywords.AvoidColumn, BreakMode.AvoidColumn},
                {Keywords.Column, BreakMode.Column},
                {Keywords.AvoidRegion, BreakMode.AvoidRegion},
                {Keywords.Region, BreakMode.Region}
            }.Freeze();
        /// <summary>css-break-3 §3.3 legacy page-break-before/page-break-after.</summary>
        public static readonly KeywordMap<BreakMode> PageBreakModes =
            new KeywordMap<BreakMode>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.Auto, BreakMode.Auto},
                {Keywords.Always, BreakMode.Always},
                {Keywords.Avoid, BreakMode.Avoid},
                {Keywords.Left, BreakMode.Left},
                {Keywords.Right, BreakMode.Right}
            }.Freeze();
        /// <summary>css-break-3 §3.2 break-inside.</summary>
        public static readonly KeywordMap<BreakMode> BreakInsideModes =
            new KeywordMap<BreakMode>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.Auto, BreakMode.Auto},
                {Keywords.Avoid, BreakMode.Avoid},
                {Keywords.AvoidPage, BreakMode.AvoidPage},
                {Keywords.AvoidColumn, BreakMode.AvoidColumn},
                {Keywords.AvoidRegion, BreakMode.AvoidRegion}
            }.Freeze();
        /// <summary>css-break-3 §3.3 legacy page-break-inside.</summary>
        public static readonly KeywordMap<BreakMode> PageBreakInsideModes =
            new KeywordMap<BreakMode>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.Auto, BreakMode.Auto},
                {Keywords.Avoid, BreakMode.Avoid}
            }.Freeze();
        public static readonly KeywordMap<float> HorizontalModes =
            new KeywordMap<float>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.Left, 0f},
                {Keywords.Center, 0.5f},
                {Keywords.Right, 1f}
            }.Freeze();
        public static readonly KeywordMap<float> VerticalModes =
            new KeywordMap<float>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.Top, 0f},
                {Keywords.Center, 0.5f},
                {Keywords.Bottom, 1f}
            }.Freeze();
        public static readonly KeywordMap<UnicodeMode> UnicodeModes =
            new KeywordMap<UnicodeMode>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.Normal, UnicodeMode.Normal},
                {Keywords.Embed, UnicodeMode.Embed},
                {Keywords.Isolate, UnicodeMode.Isolate},
                {Keywords.IsolateOverride, UnicodeMode.IsolateOverride},
                {Keywords.BidirectionalOverride, UnicodeMode.BidirectionalOverride},
                {Keywords.Plaintext, UnicodeMode.Plaintext}
            }.Freeze();
        public static readonly KeywordMap<SystemCursor> Cursors =
            new KeywordMap<SystemCursor>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.Auto, SystemCursor.Auto},
                {Keywords.Default, SystemCursor.Default},
                {Keywords.None, SystemCursor.None},
                {Keywords.ContextMenu, SystemCursor.ContextMenu},
                {Keywords.Help, SystemCursor.Help},
                {Keywords.Pointer, SystemCursor.Pointer},
                {Keywords.Progress, SystemCursor.Progress},
                {Keywords.Wait, SystemCursor.Wait},
                {Keywords.Cell, SystemCursor.Cell},
                {Keywords.Crosshair, SystemCursor.Crosshair},
                {Keywords.Text, SystemCursor.Text},
                {Keywords.VerticalText, SystemCursor.VerticalText},
                {Keywords.Alias, SystemCursor.Alias},
                {Keywords.Copy, SystemCursor.Copy},
                {Keywords.Move, SystemCursor.Move},
                {Keywords.NoDrop, SystemCursor.NoDrop},
                {Keywords.NotAllowed, SystemCursor.NotAllowed},
                {Keywords.EastResize, SystemCursor.EResize},
                {Keywords.NorthResize, SystemCursor.NResize},
                {Keywords.NorthEastResize, SystemCursor.NeResize},
                {Keywords.NorthWestResize, SystemCursor.NwResize},
                {Keywords.SouthResize, SystemCursor.SResize},
                {Keywords.SouthEastResize, SystemCursor.SeResize},
                {Keywords.SouthWestResize, SystemCursor.WResize},
                {Keywords.WestResize, SystemCursor.WResize},
                {Keywords.EastWestResize, SystemCursor.EwResize},
                {Keywords.NorthSouthResize, SystemCursor.NsResize},
                {Keywords.NorthEastSouthWestResize, SystemCursor.NeswResize},
                {Keywords.NorthWestSouthEastResize, SystemCursor.NwseResize},
                {Keywords.ColResize, SystemCursor.ColResize},
                {Keywords.RowResize, SystemCursor.RowResize},
                {Keywords.AllScroll, SystemCursor.AllScroll},
                {Keywords.ZoomIn, SystemCursor.ZoomIn},
                {Keywords.ZoomOut, SystemCursor.ZoomOut},
                {Keywords.Grab, SystemCursor.Grab},
                {Keywords.Grabbing, SystemCursor.Grabbing}
            }.Freeze();
        public static readonly KeywordMap<PositionMode> PositionModes =
            new KeywordMap<PositionMode>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.Static, PositionMode.Static},
                {Keywords.Relative, PositionMode.Relative},
                {Keywords.Absolute, PositionMode.Absolute},
                {Keywords.Sticky, PositionMode.Sticky},
                {Keywords.WebkitSticky, PositionMode.Sticky},
                {Keywords.Fixed, PositionMode.Fixed}
            }.Freeze();
        public static readonly KeywordMap<Overflow> OverflowModes =
            new KeywordMap<Overflow>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.Visible, Overflow.Visible},
                {Keywords.Hidden, Overflow.Hidden},
                {Keywords.Scroll, Overflow.Scroll},
                {Keywords.Auto, Overflow.Auto},
                {Keywords.Clip, Overflow.Clip}
            }.Freeze();
        public static readonly KeywordMap<Floating> FloatingModes =
            new KeywordMap<Floating>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.None, Floating.None},
                {Keywords.Left, Floating.Left},
                {Keywords.Right, Floating.Right},
                {Keywords.Footnote, Floating.Footnote},
                {Keywords.Top, Floating.Top},
                {Keywords.Bottom, Floating.Bottom},
                {Keywords.TopBottom, Floating.TopBottom},
                {Keywords.Snap, Floating.Snap},
                {Keywords.Inside, Floating.Inside},
                {Keywords.Outside, Floating.Outside},
                {Keywords.InlineStart, Floating.InlineStart},
                {Keywords.InlineEnd, Floating.InlineEnd}
            }.Freeze();
        // Named *References (plural), not FloatReference, to avoid colliding with the FloatReference
        // enum type itself - same convention as FloatingModes above.
        public static readonly KeywordMap<FloatReference> FloatReferences =
            new KeywordMap<FloatReference>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.Inline, FloatReference.Inline},
                {Keywords.Column, FloatReference.Column},
                {Keywords.Region, FloatReference.Region},
                {Keywords.Page, FloatReference.Page}
            }.Freeze();
        public static readonly KeywordMap<FootnoteDisplayMode> FootnoteDisplayModes =
            new KeywordMap<FootnoteDisplayMode>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.Block, FootnoteDisplayMode.Block},
                {Keywords.Inline, FootnoteDisplayMode.Inline},
                {Keywords.Compact, FootnoteDisplayMode.Compact}
            }.Freeze();
        public static readonly KeywordMap<FootnotePolicyMode> FootnotePolicyModes =
            new KeywordMap<FootnotePolicyMode>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.Auto, FootnotePolicyMode.Auto},
                {Keywords.Line, FootnotePolicyMode.Line},
                {Keywords.Block, FootnotePolicyMode.Block}
            }.Freeze();
        public static readonly KeywordMap<DisplayMode> DisplayModes =
            new KeywordMap<DisplayMode>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.None, DisplayMode.None},
                {Keywords.Contents, DisplayMode.Contents},
                {Keywords.Inline, DisplayMode.Inline},
                {Keywords.Block, DisplayMode.Block},
                {Keywords.FlowRoot, DisplayMode.FlowRoot},
                {Keywords.InlineBlock, DisplayMode.InlineBlock},
                {Keywords.ListItem, DisplayMode.ListItem},
                {Keywords.InlineTable, DisplayMode.InlineTable},
                {Keywords.Table, DisplayMode.Table},
                {Keywords.TableCaption, DisplayMode.TableCaption},
                {Keywords.TableCell, DisplayMode.TableCell},
                {Keywords.TableColumn, DisplayMode.TableColumn},
                {Keywords.TableColumnGroup, DisplayMode.TableColumnGroup},
                {Keywords.TableFooterGroup, DisplayMode.TableFooterGroup},
                {Keywords.TableHeaderGroup, DisplayMode.TableHeaderGroup},
                {Keywords.TableRow, DisplayMode.TableRow},
                {Keywords.TableRowGroup, DisplayMode.TableRowGroup},
                {Keywords.Flex, DisplayMode.Flex},
                {Keywords.InlineFlex, DisplayMode.InlineFlex},
                {Keywords.WebkitFlex, DisplayMode.Flex},
                {Keywords.WebkitInlineFlex, DisplayMode.InlineFlex},
                // The 2009 flexbox model. Resolved to block/flex (inline-*) after the cascade by LegacyBox.Resolve,
                // once -webkit-box-orient is known.
                {Keywords.WebkitBox, DisplayMode.Flex},
                {Keywords.WebkitInlineBox, DisplayMode.InlineFlex},
                {Keywords.MozBox, DisplayMode.Flex},
                {Keywords.MozInlineBox, DisplayMode.InlineFlex},
                {Keywords.Grid, DisplayMode.Grid},
                {Keywords.InlineGrid, DisplayMode.InlineGrid}
            }.Freeze();
        public static readonly KeywordMap<ClearMode> ClearModes =
            new KeywordMap<ClearMode>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.None, ClearMode.None},
                {Keywords.Left, ClearMode.Left},
                {Keywords.Right, ClearMode.Right},
                {Keywords.Both, ClearMode.Both},
                {Keywords.InlineStart, ClearMode.InlineStart},
                {Keywords.InlineEnd, ClearMode.InlineEnd}
            }.Freeze();
        public static readonly KeywordMap<BackgroundRepeat> BackgroundRepeats =
            new KeywordMap<BackgroundRepeat>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.NoRepeat, BackgroundRepeat.NoRepeat},
                {Keywords.Repeat, BackgroundRepeat.Repeat},
                {Keywords.Round, BackgroundRepeat.Round},
                {Keywords.Space, BackgroundRepeat.Space}
            }.Freeze();
        public static readonly KeywordMap<BlendMode> BlendModes =
            new KeywordMap<BlendMode>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.Color, BlendMode.Color},
                {Keywords.ColorBurn, BlendMode.ColorBurn},
                {Keywords.ColorDodge, BlendMode.ColorDodge},
                {Keywords.Darken, BlendMode.Darken},
                {Keywords.Difference, BlendMode.Difference},
                {Keywords.Exclusion, BlendMode.Exclusion},
                {Keywords.HardLight, BlendMode.HardLight},
                {Keywords.Hue, BlendMode.Hue},
                {Keywords.Lighten, BlendMode.Lighten},
                {Keywords.Luminosity, BlendMode.Luminosity},
                {Keywords.Multiply, BlendMode.Multiply},
                {Keywords.Normal, BlendMode.Normal},
                {Keywords.Overlay, BlendMode.Overlay},
                {Keywords.Saturation, BlendMode.Saturation},
                {Keywords.Screen, BlendMode.Screen},
                {Keywords.SoftLight, BlendMode.SoftLight}
            }.Freeze();
        public static readonly KeywordMap<UpdateFrequency> UpdateFrequencies =
            new KeywordMap<UpdateFrequency>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.None, UpdateFrequency.None},
                {Keywords.Slow, UpdateFrequency.Slow},
                {Keywords.Normal, UpdateFrequency.Normal}
            }.Freeze();
        public static readonly KeywordMap<ScriptingState> ScriptingStates =
            new KeywordMap<ScriptingState>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.None, ScriptingState.None},
                {Keywords.InitialOnly, ScriptingState.InitialOnly},
                {Keywords.Enabled, ScriptingState.Enabled}
            }.Freeze();
        public static readonly KeywordMap<PointerAccuracy> PointerAccuracies =
            new KeywordMap<PointerAccuracy>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.None, PointerAccuracy.None},
                {Keywords.Coarse, PointerAccuracy.Coarse},
                {Keywords.Fine, PointerAccuracy.Fine}
            }.Freeze();
        public static readonly KeywordMap<HoverAbility> HoverAbilities =
            new KeywordMap<HoverAbility>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.None, HoverAbility.None},
                {Keywords.OnDemand, HoverAbility.OnDemand},
                {Keywords.Hover, HoverAbility.Hover}
            }.Freeze();
        public static readonly KeywordMap<RadialGradient.SizeMode> RadialGradientSizeModes =
            new KeywordMap<RadialGradient.SizeMode>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.ClosestSide, RadialGradient.SizeMode.ClosestSide},
                {Keywords.FarthestSide, RadialGradient.SizeMode.FarthestSide},
                {Keywords.ClosestCorner, RadialGradient.SizeMode.ClosestCorner},
                {Keywords.FarthestCorner, RadialGradient.SizeMode.FarthestCorner}
            }.Freeze();
        public static readonly KeywordMap<ImageRenderingMode> ImageRenderingModes =
            new KeywordMap<ImageRenderingMode>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.Auto, ImageRenderingMode.Auto},
                {Keywords.Smooth, ImageRenderingMode.Smooth},
                {Keywords.HighQuality, ImageRenderingMode.HighQuality},
                {Keywords.CrispEdges, ImageRenderingMode.CrispEdges},
                {Keywords.Pixelated, ImageRenderingMode.Pixelated}
            }.Freeze();
        public static readonly KeywordMap<ObjectFitting> ObjectFittings =
            new KeywordMap<ObjectFitting>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.None, ObjectFitting.None},
                {Keywords.Cover, ObjectFitting.Cover},
                {Keywords.Contain, ObjectFitting.Contain},
                {Keywords.Fill, ObjectFitting.Fill},
                {Keywords.ScaleDown, ObjectFitting.ScaleDown}
            }.Freeze();
        public static readonly KeywordMap<FontWeight> FontWeights =
            new KeywordMap<FontWeight>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.Normal, FontWeight.Normal},
                {Keywords.Bold, FontWeight.Bold},
                {Keywords.Bolder, FontWeight.Bolder},
                {Keywords.Lighter, FontWeight.Lighter}
            }.Freeze();
        public static readonly KeywordMap<SystemFont> SystemFonts =
            new KeywordMap<SystemFont>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.Caption, SystemFont.Caption},
                {Keywords.Icon, SystemFont.Icon},
                {Keywords.Menu, SystemFont.Menu},
                {Keywords.MessageBox, SystemFont.MessageBox},
                {Keywords.SmallCaption, SystemFont.SmallCaption},
                {Keywords.StatusBar, SystemFont.StatusBar}
            }.Freeze();
        public static readonly KeywordMap<StrokeLinecap> StrokeLinecaps =
            new KeywordMap<StrokeLinecap>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.Butt, StrokeLinecap.Butt},
                {Keywords.Round, StrokeLinecap.Round},
                {Keywords.Square, StrokeLinecap.Square}
            }.Freeze();
        public static readonly KeywordMap<StrokeLinejoin> StrokeLinejoins =
            new KeywordMap<StrokeLinejoin>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.Miter, StrokeLinejoin.Miter},
                {Keywords.Round, StrokeLinejoin.Round},
                {Keywords.Bevel, StrokeLinejoin.Bevel}
            }.Freeze();
        public static readonly KeywordMap<WordBreak> WordBreaks =
            new KeywordMap<WordBreak>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.Normal, WordBreak.Normal},
                {Keywords.BreakAll, WordBreak.BreakAll},
                {Keywords.KeepAll, WordBreak.KeepAll}
            }.Freeze();
        public static readonly KeywordMap<LineBreak> LineBreaks =
            new KeywordMap<LineBreak>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.Auto, LineBreak.Auto},
                {Keywords.Loose, LineBreak.Loose},
                {Keywords.Normal, LineBreak.Normal},
                {Keywords.Strict, LineBreak.Strict},
                {Keywords.Anywhere, LineBreak.Anywhere}
            }.Freeze();
        public static readonly KeywordMap<OverflowWrap> OverflowWraps =
            new KeywordMap<OverflowWrap>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.Normal, OverflowWrap.Normal},
                {Keywords.BreakWord, OverflowWrap.BreakWord},
                {Keywords.Anywhere, OverflowWrap.Anywhere}
            }.Freeze();
        public static readonly KeywordMap<FillRule> FillRules =
            new KeywordMap<FillRule>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.Nonzero, FillRule.Nonzero},
                {Keywords.Evenodd, FillRule.Evenodd}
            }.Freeze();

        public static readonly KeywordMap<FlexDirection> FlexDirections =
            new KeywordMap<FlexDirection>(StringComparer.OrdinalIgnoreCase)
            {
                { Keywords.Row, FlexDirection.Row },
                { Keywords.RowReverse, FlexDirection.RowReverse },
                { Keywords.Column, FlexDirection.Column },
                { Keywords.ColumnReverse, FlexDirection.ColumnReverse }
            }.Freeze();

        public static readonly KeywordMap<FlexWrap> FlexWraps =
            new KeywordMap<FlexWrap>(StringComparer.OrdinalIgnoreCase)
            {
                { Keywords.Nowrap, FlexWrap.NoWrap },
                { Keywords.Wrap, FlexWrap.Wrap },
                { Keywords.WrapReverse, FlexWrap.WrapReverse }
            }.Freeze();

        public static readonly KeywordMap<IntrinsicSizing> IntrinsicSizings =
            new KeywordMap<IntrinsicSizing>(StringComparer.OrdinalIgnoreCase)
            {
                { Keywords.MaxContent, IntrinsicSizing.MaxContent },
                { Keywords.MinContent, IntrinsicSizing.MinContent },
                { Keywords.FitContent, IntrinsicSizing.FitContent },
                { Keywords.Content, IntrinsicSizing.Content }
            }.Freeze();

        public static readonly KeywordMap<AlignContent> AlignContents =
            new KeywordMap<AlignContent>(StringComparer.OrdinalIgnoreCase)
            {
                { Keywords.Center, AlignContent.Center },
                { Keywords.Start, AlignContent.Start },
                { Keywords.End, AlignContent.End },
                { Keywords.FlexStart, AlignContent.FlexStart },
                { Keywords.FlexEnd, AlignContent.FlexEnd },
                { Keywords.Normal, AlignContent.Normal },
                { Keywords.Baseline, AlignContent.Baseline },
                { Keywords.SpaceBetween, AlignContent.SpaceBetween },
                { Keywords.SpaceAround, AlignContent.SpaceAround },
                { Keywords.SpaceEvenly, AlignContent.SpaceEvenly },
                { Keywords.Stretch, AlignContent.Stretch },
            }.Freeze();

        public static readonly KeywordMap<AlignItem> AlignItems =
            new KeywordMap<AlignItem>(StringComparer.OrdinalIgnoreCase)
            {
                { Keywords.Normal, AlignItem.Normal },
                { Keywords.Stretch, AlignItem.Stretch },
                { Keywords.Center, AlignItem.Center },
                { Keywords.Start, AlignItem.Start },
                { Keywords.End, AlignItem.End },
                { Keywords.FlexStart, AlignItem.FlexStart },
                { Keywords.FlexEnd, AlignItem.FlexEnd },
                { Keywords.SelfStart, AlignItem.SelfStart },
                { Keywords.SelfEnd, AlignItem.SelfEnd },
                { Keywords.Baseline, AlignItem.Baseline },
            }.Freeze();

        // The four maps below back CssBox's cascaded align-items/align-self/justify-content/align-content
        // storage. CssLayoutEngineFlex/CssLayoutEngineGrid dispatch every keyword listed here - see
        // CssLayoutEngineFlex.ComputeMainOffsets/DistributeCrossSpace/ComputeCrossOffsets and
        // CssLayoutEngineGrid.PositionTracks/AlignmentOffset (issues #644/#645).
        public static readonly KeywordMap<AlignItem> AlignItemKeywords =
            new KeywordMap<AlignItem>(StringComparer.OrdinalIgnoreCase)
            {
                { Keywords.FlexStart, AlignItem.FlexStart },
                { Keywords.FlexEnd, AlignItem.FlexEnd },
                { Keywords.Start, AlignItem.Start },
                { Keywords.End, AlignItem.End },
                { Keywords.SelfStart, AlignItem.SelfStart },
                { Keywords.SelfEnd, AlignItem.SelfEnd },
                { Keywords.Center, AlignItem.Center },
                { Keywords.Stretch, AlignItem.Stretch },
                { Keywords.Normal, AlignItem.Normal },
                { Keywords.Baseline, AlignItem.Baseline }
            }.Freeze();

        public static readonly KeywordMap<AlignItem> AlignSelfKeywords =
            new KeywordMap<AlignItem>(StringComparer.OrdinalIgnoreCase)
            {
                { Keywords.Auto, AlignItem.Auto },
                { Keywords.FlexStart, AlignItem.FlexStart },
                { Keywords.FlexEnd, AlignItem.FlexEnd },
                { Keywords.Start, AlignItem.Start },
                { Keywords.End, AlignItem.End },
                { Keywords.SelfStart, AlignItem.SelfStart },
                { Keywords.SelfEnd, AlignItem.SelfEnd },
                { Keywords.Center, AlignItem.Center },
                { Keywords.Stretch, AlignItem.Stretch },
                { Keywords.Normal, AlignItem.Normal },
                { Keywords.Baseline, AlignItem.Baseline }
            }.Freeze();

        public static readonly KeywordMap<JustifyContent> JustifyContentKeywords =
            new KeywordMap<JustifyContent>(StringComparer.OrdinalIgnoreCase)
            {
                { Keywords.Normal, JustifyContent.Normal },
                { Keywords.FlexStart, JustifyContent.FlexStart },
                { Keywords.FlexEnd, JustifyContent.FlexEnd },
                { Keywords.Start, JustifyContent.Start },
                { Keywords.End, JustifyContent.End },
                { Keywords.Left, JustifyContent.Left },
                { Keywords.Right, JustifyContent.Right },
                { Keywords.Center, JustifyContent.Center },
                { Keywords.SpaceBetween, JustifyContent.SpaceBetween },
                { Keywords.SpaceAround, JustifyContent.SpaceAround },
                { Keywords.SpaceEvenly, JustifyContent.SpaceEvenly },
                { Keywords.Stretch, JustifyContent.Stretch }
            }.Freeze();

        public static readonly KeywordMap<AlignContent> AlignContentKeywords =
            new KeywordMap<AlignContent>(StringComparer.OrdinalIgnoreCase)
            {
                { Keywords.FlexStart, AlignContent.FlexStart },
                { Keywords.FlexEnd, AlignContent.FlexEnd },
                { Keywords.Start, AlignContent.Start },
                { Keywords.End, AlignContent.End },
                { Keywords.Center, AlignContent.Center },
                { Keywords.SpaceBetween, AlignContent.SpaceBetween },
                { Keywords.SpaceAround, AlignContent.SpaceAround },
                { Keywords.SpaceEvenly, AlignContent.SpaceEvenly },
                { Keywords.Stretch, AlignContent.Stretch },
                { Keywords.Baseline, AlignContent.Baseline },
                { Keywords.Normal, AlignContent.Normal }
            }.Freeze();

        public static readonly KeywordMap<JustifyItem> JustifyItemKeywords =
            new KeywordMap<JustifyItem>(StringComparer.OrdinalIgnoreCase)
            {
                { Keywords.Start, JustifyItem.Start },
                { Keywords.End, JustifyItem.End },
                { Keywords.SelfEnd, JustifyItem.SelfEnd },
                { Keywords.Center, JustifyItem.Center },
                { Keywords.Stretch, JustifyItem.Stretch },
                { Keywords.Normal, JustifyItem.Normal },
                { Keywords.Left, JustifyItem.Left },
                { Keywords.Right, JustifyItem.Right }
            }.Freeze();

        public static readonly KeywordMap<JustifyItem> JustifySelfKeywords =
            new KeywordMap<JustifyItem>(StringComparer.OrdinalIgnoreCase)
            {
                { Keywords.Auto, JustifyItem.Auto },
                { Keywords.Start, JustifyItem.Start },
                { Keywords.End, JustifyItem.End },
                { Keywords.SelfEnd, JustifyItem.SelfEnd },
                { Keywords.Center, JustifyItem.Center },
                { Keywords.Stretch, JustifyItem.Stretch },
                { Keywords.Normal, JustifyItem.Normal },
                { Keywords.Left, JustifyItem.Left },
                { Keywords.Right, JustifyItem.Right }
            }.Freeze();

        public static readonly KeywordMap<ContainerType> ContainerTypes =
            new KeywordMap<ContainerType>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.Normal, ContainerType.Normal},
                {Keywords.Size, ContainerType.Size},
                {Keywords.InlineSize, ContainerType.InlineSize}
            }.Freeze();
    }
}
