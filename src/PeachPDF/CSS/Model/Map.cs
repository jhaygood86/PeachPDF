using System;
using System.Collections.Frozen;
using System.Collections.Generic;

namespace PeachPDF.CSS
{
    internal static class Map
    {
        public static readonly FrozenDictionary<string, Whitespace> WhitespaceModes =
            new Dictionary<string, Whitespace>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.Normal, Whitespace.Normal},
                {Keywords.Pre, Whitespace.Pre},
                {Keywords.Nowrap, Whitespace.NoWrap},
                {Keywords.PreWrap, Whitespace.PreWrap},
                {Keywords.PreLine, Whitespace.PreLine}
            }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        public static readonly FrozenDictionary<string, TextTransform> TextTransforms =
            new Dictionary<string, TextTransform>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.None, TextTransform.None},
                {Keywords.Capitalize, TextTransform.Capitalize},
                {Keywords.Uppercase, TextTransform.Uppercase},
                {Keywords.Lowercase, TextTransform.Lowercase},
                {Keywords.FullWidth, TextTransform.FullWidth}
            }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        public static readonly FrozenDictionary<string, TextAlignLast> TextAlignmentsLast =
            new Dictionary<string, TextAlignLast>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.Auto, TextAlignLast.Auto},
                {Keywords.Start, TextAlignLast.Start},
                {Keywords.End, TextAlignLast.End},
                {Keywords.Right, TextAlignLast.Right},
                {Keywords.Left, TextAlignLast.Left},
                {Keywords.Center, TextAlignLast.Center},
                {Keywords.Justify, TextAlignLast.Justify}
            }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        public static readonly FrozenDictionary<string, TextAnchor> TextAnchors =
            new Dictionary<string, TextAnchor>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.Start, TextAnchor.Start},
                {Keywords.Middle, TextAnchor.Middle},
                {Keywords.End, TextAnchor.End}
            }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        public static readonly FrozenDictionary<string, TextJustify> TextJustifyOptions =
            new Dictionary<string, TextJustify>(StringComparer.OrdinalIgnoreCase)
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
            }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        public static readonly FrozenDictionary<string, JustifyContent> JustifyContentOptions =
            new Dictionary<string, JustifyContent>(StringComparer.OrdinalIgnoreCase)
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
            }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        public static readonly FrozenDictionary<string, HorizontalAlignment> HorizontalAlignments =
            new Dictionary<string, HorizontalAlignment>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.Left, HorizontalAlignment.Left},
                {Keywords.Right, HorizontalAlignment.Right},
                {Keywords.Center, HorizontalAlignment.Center},
                {Keywords.Justify, HorizontalAlignment.Justify}
            }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        public static readonly FrozenDictionary<string, VerticalAlignment> VerticalAlignments =
            new Dictionary<string, VerticalAlignment>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.Baseline, VerticalAlignment.Baseline},
                {Keywords.Sub, VerticalAlignment.Sub},
                {Keywords.Super, VerticalAlignment.Super},
                {Keywords.TextTop, VerticalAlignment.TextTop},
                {Keywords.TextBottom, VerticalAlignment.TextBottom},
                {Keywords.Middle, VerticalAlignment.Middle},
                {Keywords.Top, VerticalAlignment.Top},
                {Keywords.Bottom, VerticalAlignment.Bottom}
            }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        public static readonly FrozenDictionary<string, LineStyle> LineStyles =
            new Dictionary<string, LineStyle>(StringComparer.OrdinalIgnoreCase)
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
            }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        public static readonly FrozenDictionary<string, PdfTagType> PdfTagTypes =
            new Dictionary<string, PdfTagType>(StringComparer.OrdinalIgnoreCase)
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
            }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        public static readonly FrozenDictionary<string, BoxModel> BoxModels =
            new Dictionary<string, BoxModel>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.BorderBox, BoxModel.BorderBox},
                {Keywords.PaddingBox, BoxModel.PaddingBox},
                {Keywords.ContentBox, BoxModel.ContentBox}
            }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        public static readonly FrozenDictionary<string, ITimingFunction> TimingFunctions =
            new Dictionary<string, ITimingFunction>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.Ease, new CubicBezierTimingFunction(0.25f, 0.1f, 0.25f, 1f)},
                {Keywords.EaseIn, new CubicBezierTimingFunction(0.42f, 0f, 1f, 1f)},
                {Keywords.EaseOut, new CubicBezierTimingFunction(0f, 0f, 0.58f, 1f)},
                {Keywords.EaseInOut, new CubicBezierTimingFunction(0.42f, 0f, 0.58f, 1f)},
                {Keywords.Linear, new CubicBezierTimingFunction(0f, 0f, 1f, 1f)},
                {Keywords.StepStart, new StepsTimingFunction(1, true)},
                {Keywords.StepEnd, new StepsTimingFunction(1)}
            }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        public static readonly FrozenDictionary<string, AnimationFillStyle> AnimationFillStyles =
            new Dictionary<string, AnimationFillStyle>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.None, AnimationFillStyle.None},
                {Keywords.Forwards, AnimationFillStyle.Forwards},
                {Keywords.Backwards, AnimationFillStyle.Backwards},
                {Keywords.Both, AnimationFillStyle.Both}
            }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        public static readonly FrozenDictionary<string, AnimationDirection> AnimationDirections =
            new Dictionary<string, AnimationDirection>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.Normal, AnimationDirection.Normal},
                {Keywords.Reverse, AnimationDirection.Reverse},
                {Keywords.Alternate, AnimationDirection.Alternate},
                {Keywords.AlternateReverse, AnimationDirection.AlternateReverse}
            }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        public static readonly FrozenDictionary<string, Visibility> Visibilities =
            new Dictionary<string, Visibility>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.Visible, Visibility.Visible},
                {Keywords.Hidden, Visibility.Hidden},
                {Keywords.Collapse, Visibility.Collapse}
            }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        public static readonly FrozenDictionary<string, PlayState> PlayStates =
            new Dictionary<string, PlayState>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.Running, PlayState.Running},
                {Keywords.Paused, PlayState.Paused}
            }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        public static readonly FrozenDictionary<string, FontVariant> FontVariants =
            new Dictionary<string, FontVariant>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.Normal, FontVariant.Normal},
                {Keywords.SmallCaps, FontVariant.SmallCaps}
            }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        public static readonly FrozenDictionary<string, DirectionMode> DirectionModes =
            new Dictionary<string, DirectionMode>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.Ltr, DirectionMode.Ltr},
                {Keywords.Rtl, DirectionMode.Rtl}
            }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        public static readonly FrozenDictionary<string, ListStyle> ListStyles =
            new Dictionary<string, ListStyle>(StringComparer.OrdinalIgnoreCase)
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
                {Keywords.Georgian, ListStyle.Georgian},
                {Keywords.LowerAlpha, ListStyle.LowerLatin},
                {Keywords.UpperAlpha, ListStyle.UpperLatin},
                {Keywords.None, ListStyle.None}
            }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        public static readonly FrozenDictionary<string, ListPosition> ListPositions =
            new Dictionary<string, ListPosition>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.Inside, ListPosition.Inside},
                {Keywords.Outside, ListPosition.Outside}
            }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        public static readonly FrozenDictionary<string, FontSize> FontSizes =
            new Dictionary<string, FontSize>(StringComparer.OrdinalIgnoreCase)
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
            }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        public static readonly FrozenDictionary<string, TextDecorationStyle> TextDecorationStyles =
            new Dictionary<string, TextDecorationStyle>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.Solid, TextDecorationStyle.Solid},
                {Keywords.Double, TextDecorationStyle.Double},
                {Keywords.Dotted, TextDecorationStyle.Dotted},
                {Keywords.Dashed, TextDecorationStyle.Dashed},
                {Keywords.Wavy, TextDecorationStyle.Wavy}
            }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        public static readonly FrozenDictionary<string, TextDecorationLine> TextDecorationLines =
            new Dictionary<string, TextDecorationLine>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.Underline, TextDecorationLine.Underline},
                {Keywords.Overline, TextDecorationLine.Overline},
                {Keywords.LineThrough, TextDecorationLine.LineThrough},
                {Keywords.Blink, TextDecorationLine.Blink}
            }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        public static readonly FrozenDictionary<string, BorderRepeat> BorderRepeatModes =
            new Dictionary<string, BorderRepeat>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.Stretch, BorderRepeat.Stretch},
                {Keywords.Repeat, BorderRepeat.Repeat},
                {Keywords.Round, BorderRepeat.Round}
            }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        public static readonly FrozenDictionary<string, string> DefaultFontFamilies =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.Serif, "Times New Roman"},
                {Keywords.SansSerif, "Arial"},
                {Keywords.Monospace, "Consolas"},
                {Keywords.Cursive, "Cursive"},
                {Keywords.Fantasy, "Comic Sans"}
            }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        public static readonly FrozenDictionary<string, BackgroundAttachment> BackgroundAttachments =
            new Dictionary<string, BackgroundAttachment>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.Fixed, BackgroundAttachment.Fixed},
                {Keywords.Local, BackgroundAttachment.Local},
                {Keywords.Scroll, BackgroundAttachment.Scroll}
            }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        public static readonly FrozenDictionary<string, FontStyle> FontStyles =
            new Dictionary<string, FontStyle>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.Normal, FontStyle.Normal},
                {Keywords.Italic, FontStyle.Italic},
                {Keywords.Oblique, FontStyle.Oblique}
            }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        public static readonly FrozenDictionary<string, FontStretch> FontStretches =
            new Dictionary<string, FontStretch>(StringComparer.OrdinalIgnoreCase)
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
            }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        public static readonly FrozenDictionary<string, BreakMode> BreakModes =
            new Dictionary<string, BreakMode>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.Auto, BreakMode.Auto},
                {Keywords.Always, BreakMode.Always},
                {Keywords.Avoid, BreakMode.Avoid},
                {Keywords.Left, BreakMode.Left},
                {Keywords.Right, BreakMode.Right},
                {Keywords.Page, BreakMode.Page},
                {Keywords.Column, BreakMode.Column},
                {Keywords.AvoidPage, BreakMode.AvoidPage},
                {Keywords.AvoidColumn, BreakMode.AvoidColumn}
            }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        public static readonly FrozenDictionary<string, BreakMode> PageBreakModes =
            new Dictionary<string, BreakMode>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.Auto, BreakMode.Auto},
                {Keywords.Always, BreakMode.Always},
                {Keywords.Avoid, BreakMode.Avoid},
                {Keywords.Left, BreakMode.Left},
                {Keywords.Right, BreakMode.Right}
            }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        public static readonly FrozenDictionary<string, BreakMode> BreakInsideModes =
            new Dictionary<string, BreakMode>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.Auto, BreakMode.Auto},
                {Keywords.Avoid, BreakMode.Avoid},
                {Keywords.AvoidPage, BreakMode.AvoidPage},
                {Keywords.AvoidColumn, BreakMode.AvoidColumn},
                {Keywords.AvoidRegion, BreakMode.AvoidRegion}
            }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        public static readonly FrozenDictionary<string, float> HorizontalModes =
            new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.Left, 0f},
                {Keywords.Center, 0.5f},
                {Keywords.Right, 1f}
            }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        public static readonly FrozenDictionary<string, float> VerticalModes =
            new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.Top, 0f},
                {Keywords.Center, 0.5f},
                {Keywords.Bottom, 1f}
            }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        public static readonly FrozenDictionary<string, UnicodeMode> UnicodeModes =
            new Dictionary<string, UnicodeMode>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.Normal, UnicodeMode.Normal},
                {Keywords.Embed, UnicodeMode.Embed},
                {Keywords.Isolate, UnicodeMode.Isolate},
                {Keywords.IsolateOverride, UnicodeMode.IsolateOverride},
                {Keywords.BidirectionalOverride, UnicodeMode.BidirectionalOverride},
                {Keywords.Plaintext, UnicodeMode.Plaintext}
            }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        public static readonly FrozenDictionary<string, SystemCursor> Cursors =
            new Dictionary<string, SystemCursor>(StringComparer.OrdinalIgnoreCase)
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
            }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        public static readonly FrozenDictionary<string, PositionMode> PositionModes =
            new Dictionary<string, PositionMode>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.Static, PositionMode.Static},
                {Keywords.Relative, PositionMode.Relative},
                {Keywords.Absolute, PositionMode.Absolute},
                {Keywords.Sticky, PositionMode.Sticky},
                {Keywords.Fixed, PositionMode.Fixed}
            }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        public static readonly FrozenDictionary<string, Overflow> OverflowModes =
            new Dictionary<string, Overflow>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.Visible, Overflow.Visible},
                {Keywords.Hidden, Overflow.Hidden},
                {Keywords.Scroll, Overflow.Scroll},
                {Keywords.Auto, Overflow.Auto}
            }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        public static readonly FrozenDictionary<string, Floating> FloatingModes =
            new Dictionary<string, Floating>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.None, Floating.None},
                {Keywords.Left, Floating.Left},
                {Keywords.Right, Floating.Right}
            }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        public static readonly FrozenDictionary<string, DisplayMode> DisplayModes =
            new Dictionary<string, DisplayMode>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.None, DisplayMode.None},
                {Keywords.Inline, DisplayMode.Inline},
                {Keywords.Block, DisplayMode.Block},
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
                {Keywords.Grid, DisplayMode.Grid},
                {Keywords.InlineGrid, DisplayMode.InlineGrid}
            }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        public static readonly FrozenDictionary<string, ClearMode> ClearModes =
            new Dictionary<string, ClearMode>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.None, ClearMode.None},
                {Keywords.Left, ClearMode.Left},
                {Keywords.Right, ClearMode.Right},
                {Keywords.Both, ClearMode.Both}
            }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        public static readonly FrozenDictionary<string, BackgroundRepeat> BackgroundRepeats =
            new Dictionary<string, BackgroundRepeat>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.NoRepeat, BackgroundRepeat.NoRepeat},
                {Keywords.Repeat, BackgroundRepeat.Repeat},
                {Keywords.Round, BackgroundRepeat.Round},
                {Keywords.Space, BackgroundRepeat.Space}
            }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        public static readonly FrozenDictionary<string, BlendMode> BlendModes =
            new Dictionary<string, BlendMode>(StringComparer.OrdinalIgnoreCase)
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
            }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        public static readonly FrozenDictionary<string, UpdateFrequency> UpdateFrequencies =
            new Dictionary<string, UpdateFrequency>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.None, UpdateFrequency.None},
                {Keywords.Slow, UpdateFrequency.Slow},
                {Keywords.Normal, UpdateFrequency.Normal}
            }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        public static readonly FrozenDictionary<string, ScriptingState> ScriptingStates =
            new Dictionary<string, ScriptingState>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.None, ScriptingState.None},
                {Keywords.InitialOnly, ScriptingState.InitialOnly},
                {Keywords.Enabled, ScriptingState.Enabled}
            }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        public static readonly FrozenDictionary<string, PointerAccuracy> PointerAccuracies =
            new Dictionary<string, PointerAccuracy>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.None, PointerAccuracy.None},
                {Keywords.Coarse, PointerAccuracy.Coarse},
                {Keywords.Fine, PointerAccuracy.Fine}
            }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        public static readonly FrozenDictionary<string, HoverAbility> HoverAbilities =
            new Dictionary<string, HoverAbility>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.None, HoverAbility.None},
                {Keywords.OnDemand, HoverAbility.OnDemand},
                {Keywords.Hover, HoverAbility.Hover}
            }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        public static readonly FrozenDictionary<string, RadialGradient.SizeMode> RadialGradientSizeModes =
            new Dictionary<string, RadialGradient.SizeMode>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.ClosestSide, RadialGradient.SizeMode.ClosestSide},
                {Keywords.FarthestSide, RadialGradient.SizeMode.FarthestSide},
                {Keywords.ClosestCorner, RadialGradient.SizeMode.ClosestCorner},
                {Keywords.FarthestCorner, RadialGradient.SizeMode.FarthestCorner}
            }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        public static readonly FrozenDictionary<string, ObjectFitting> ObjectFittings =
            new Dictionary<string, ObjectFitting>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.None, ObjectFitting.None},
                {Keywords.Cover, ObjectFitting.Cover},
                {Keywords.Contain, ObjectFitting.Contain},
                {Keywords.Fill, ObjectFitting.Fill},
                {Keywords.ScaleDown, ObjectFitting.ScaleDown}
            }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        public static readonly FrozenDictionary<string, FontWeight> FontWeights =
            new Dictionary<string, FontWeight>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.Normal, FontWeight.Normal},
                {Keywords.Bold, FontWeight.Bold},
                {Keywords.Bolder, FontWeight.Bolder},
                {Keywords.Lighter, FontWeight.Lighter}
            }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        public static readonly FrozenDictionary<string, SystemFont> SystemFonts =
            new Dictionary<string, SystemFont>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.Caption, SystemFont.Caption},
                {Keywords.Icon, SystemFont.Icon},
                {Keywords.Menu, SystemFont.Menu},
                {Keywords.MessageBox, SystemFont.MessageBox},
                {Keywords.SmallCaption, SystemFont.SmallCaption},
                {Keywords.StatusBar, SystemFont.StatusBar}
            }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        public static readonly FrozenDictionary<string, StrokeLinecap> StrokeLinecaps =
            new Dictionary<string, StrokeLinecap>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.Butt, StrokeLinecap.Butt},
                {Keywords.Round, StrokeLinecap.Round},
                {Keywords.Square, StrokeLinecap.Square}
            }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        public static readonly FrozenDictionary<string, StrokeLinejoin> StrokeLinejoins =
            new Dictionary<string, StrokeLinejoin>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.Miter, StrokeLinejoin.Miter},
                {Keywords.Round, StrokeLinejoin.Round},
                {Keywords.Bevel, StrokeLinejoin.Bevel}
            }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        public static readonly FrozenDictionary<string, WordBreak> WordBreaks =
            new Dictionary<string, WordBreak>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.Normal, WordBreak.Normal},
                {Keywords.BreakAll, WordBreak.BreakAll},
                {Keywords.KeepAll, WordBreak.KeepAll}
            }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        public static readonly FrozenDictionary<string, OverflowWrap> OverflowWraps =
            new Dictionary<string, OverflowWrap>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.Normal, OverflowWrap.Normal},
                {Keywords.BreakWord, OverflowWrap.BreakWord}
            }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        public static readonly FrozenDictionary<string, FillRule> FillRules =
            new Dictionary<string, FillRule>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.Nonzero, FillRule.Nonzero},
                {Keywords.Evenodd, FillRule.Evenodd}
            }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

        public static readonly FrozenDictionary<string, FlexDirection> FlexDirections =
            new Dictionary<string, FlexDirection>(StringComparer.OrdinalIgnoreCase)
            {
                { Keywords.Row, FlexDirection.Row },
                { Keywords.RowReverse, FlexDirection.RowReverse },
                { Keywords.Column, FlexDirection.Column },
                { Keywords.ColumnReverse, FlexDirection.ColumnReverse }
            }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

        public static readonly FrozenDictionary<string, FlexWrap> FlexWraps =
            new Dictionary<string, FlexWrap>(StringComparer.OrdinalIgnoreCase)
            {
                { Keywords.Nowrap, FlexWrap.NoWrap },
                { Keywords.Wrap, FlexWrap.Wrap },
                { Keywords.WrapReverse, FlexWrap.WrapReverse }
            }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

        public static readonly FrozenDictionary<string, IntrinsicSizing> IntrinsicSizings =
            new Dictionary<string, IntrinsicSizing>(StringComparer.OrdinalIgnoreCase)
            {
                { Keywords.MaxContent, IntrinsicSizing.MaxContent },
                { Keywords.MinContent, IntrinsicSizing.MinContent },
                { Keywords.FitContent, IntrinsicSizing.FitContent },
                { Keywords.Content, IntrinsicSizing.Content }
            }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

        public static readonly FrozenDictionary<string, AlignContent> AlignContents =
            new Dictionary<string, AlignContent>(StringComparer.OrdinalIgnoreCase)
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
            }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

        public static readonly FrozenDictionary<string, AlignItem> AlignItems =
            new Dictionary<string, AlignItem>(StringComparer.OrdinalIgnoreCase)
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
            }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

        public static readonly FrozenDictionary<string, ContainerType> ContainerTypes =
            new Dictionary<string, ContainerType>(StringComparer.OrdinalIgnoreCase)
            {
                {Keywords.Normal, ContainerType.Normal},
                {Keywords.Size, ContainerType.Size},
                {Keywords.InlineSize, ContainerType.InlineSize}
            }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    }
}