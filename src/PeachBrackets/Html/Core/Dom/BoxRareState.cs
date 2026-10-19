using PeachDrawing.Core;
using PeachDrawing.Text.Shaping;
using PeachDrawing.Text.Unicode;
using PeachBrackets.Css;
using PeachBrackets.Html.Entities;
using PeachBrackets.Html.Fragmentation;
using PeachBrackets.Html.Fragments;
using PeachBrackets.Html.Handlers;
using PeachBrackets.Html.Paint;
using PeachBrackets.Html.Parse;
using PeachBrackets.Html.Utils;
using System.Collections.Generic;

namespace PeachBrackets.Html.Dom
{
    /// <summary>
    /// The per-box state a <see cref="CssBox"/> only has when it takes part in something unusual: a table's collapsed borders, a
    /// forced or avoided break, a float that straddled a page, a pseudo-element's links, a logical-property declaration, a grid
    /// item's pinned size, and the rest. Measured over benchmark documents, none of it is present on 99% of boxes, yet as plain
    /// fields it was three quarters of every box's size. <see cref="CssBox"/> reaches it through properties of the same names that
    /// read the default without allocating and allocate this object only when given a value other than that default.
    /// </summary>
    internal sealed class BoxRareState
    {
        internal bool _tableFixed;
        internal CssImage? ContentImage;
        internal bool HasPendingTargetPageContent;
        internal CssBox? DomParentBox;
        internal int? QuoteDepthAtStart;
        internal int? QuoteSubtreeRawDelta;
        internal int? QuoteSubtreeLocalMin;
        internal NamedPageElement? RegisteredNamedPageElement;
        internal RunningElement? RegisteredRunningElement;
        internal double _appliedPositionedAutoMarginTop;
        internal bool IsRoot;
        internal bool IsBeforePseudoElement;
        internal bool IsAfterPseudoElement;
        internal bool IsPlaceholderPseudoElement;
        internal bool IsMarkerPseudoElement;
        internal bool IsFootnoteCallPseudoElement;
        internal bool IsFootnoteMarkerPseudoElement;
        internal CssBox? FootnoteSourceBox;
        internal bool IsFirstLetterPseudoElement;
        internal CssBox? FirstLetterOriginatingBox;
        internal bool FirstLetterProcessed;
        internal bool MatchesFirstLetterSelector;
        internal CssBox? ResolvedFirstLineStyle;
        internal CssBox? ResolvedPlaceholderStyle;
        internal bool SuppressOwnBackgroundPaint;
        internal bool SuppressOwnBorderPaint;
        internal CssBox? TableGridDecorationBox;
        internal bool IsTableGridDecorationBox;
        internal bool IsInlineRunWrapper;
        internal bool IsReplacedBlockWrapper;
        internal bool IsInlineBoxSplitByBlock;
        internal Dictionary<int, double>? PageBreakBottoms;
        internal TableGrid? CollapsedBorderGrid;
        internal CollapsedBorderModel? CollapsedBorders;
        internal List<CollapsedBorderSegment>? CollapsedBorderSegments;
        internal TableBreakToken? TableContinuation;
        internal TableSetup? TableSetup;
        internal IReadOnlyDictionary<CssBox, double>? RowHeightRedistribution;
        internal double? NaturalRowAxisExtentCarry;
        internal List<(double X, double Top, double Bottom)>? ColumnRuleSegments;
        internal double? AlgorithmicDefiniteHeight;
        internal HashSet<string>? BuilderSetProperties;
        internal bool IsFragmentStyled;
        internal UseCategory[]? UseCategories;
        internal KhmerCategory[]? KhmerCategories;
        internal List<(CssBox Owner, int Index, CssRect Word)>? LineClampPoppedWords;
        internal (CssBox Owner, CssRect Word)? LineClampEllipsisWord;
        internal bool PendingAutoDirection;
        internal string? SectionPageCounterSectionId;
        internal bool SectionPageCounterIsTotal;
        internal string? SectionBeginId;
        internal string? SectionEndId;
        internal Rect? EmptyInlineContainingBlock;
        internal bool _isForcedBreak;
        internal bool FootnotePolicyForcedBreakBefore;
        internal bool _adjoinsForcedBreakPoint;
        internal bool _shouldRegisterPage;
        internal PageSide _forcedBreakSide;
        internal bool PlacedByForcedBreak;
        internal (double Left, double Width)? ColumnsBesideFloats;
        internal List<CssBox>? FloatsOfTheDiscardedLine;
        internal List<CssBox>? FloatsKeptAtTheStop;
        internal bool HasClearance;
        internal BreakToken? PendingBreakToken;
        internal bool _widowsRewindTaken;
        internal double? RequestedBreakBeforeTop;
        internal int RequestedBreakBeforeSlot;
        internal bool RequestedBreakEscapesNestedFragmentainer;
        internal bool _escapedForcedBreakPending;
        internal int? _escapedForcedBreakBlankSlot;
        internal BreakToken? _incomingToken;
        internal double? _resumeTopOverride;
        internal double _measureResolvedAgainst = double.NaN;
        internal bool _awaitingRefill;
        internal double? _earlyBreakRetryTop;
        internal double? _firstChildRestartedTop;
        internal (CssBox Float, int Slot)? _cursorToRestoreAfterFloat;
        internal List<CssBox>? _pendingCrossAxisRtlReflection;
        internal List<(CssBox Box, double InlineFromBottom)>? _pendingBottomFloats;
        internal CssBox.VerticalFloatReach? VerticalFloatOccupancy;
        internal bool _pendingVerticalInlineFinalize;
        internal bool _earlyBreakTaken;
        internal bool _avoidRelaxed;
        internal bool _orphansBreakTaken;
        internal EarlyBreak? _requestedChildRestart;
        internal bool _canRestartChildLoop;
        internal int _liveChildStartIndex;
        internal bool FragmentedAcrossFloatPasses;
        internal PaintPoint? FirstColumnFragmentOrigin;
        internal bool PositionAssignedByEngine;
        internal bool ItemContentSizeEverPinned;
        internal string? WidthBeforeItemPin;
        internal string? HeightBeforeItemPin;
        internal bool _resumingIntoTheNextFragmentainer;
        internal double? _groupBlockStartMarginOverride;
        internal bool IsDisplayContentsShell;

        /// <summary>The CSS animations of an element of an inline <c>&lt;svg&gt;</c>, planned once (see <see cref="CssBox.GetSvgAnimationPlan"/>).</summary>
        internal Animation.AnimationApplier.ElementPlan? SvgAnimationPlan;
        internal bool SvgAnimationPlanBuilt;
        internal List<CssBox>? DisplayContentsLiftedChildren;
        internal List<CssBox>? DisplayContentsAncestors;
        internal CssBox? OriginatingBox;
        internal string? MarginBlockStart;
        internal string? MarginBlockEnd;
        internal string? MarginInlineStart;
        internal string? MarginInlineEnd;
        internal string? PaddingBlockStart;
        internal string? PaddingBlockEnd;
        internal string? PaddingInlineStart;
        internal string? PaddingInlineEnd;
        internal string? InsetBlockStart;
        internal string? InsetBlockEnd;
        internal string? InsetInlineStart;
        internal string? InsetInlineEnd;
        internal string? BorderBlockStartWidth;
        internal string? BorderBlockStartStyle;
        internal string? BorderBlockStartColor;
        internal string? BorderBlockEndWidth;
        internal string? BorderBlockEndStyle;
        internal string? BorderBlockEndColor;
        internal string? BorderInlineStartWidth;
        internal string? BorderInlineStartStyle;
        internal string? BorderInlineStartColor;
        internal string? BorderInlineEndWidth;
        internal string? BorderInlineEndStyle;
        internal string? BorderInlineEndColor;
        internal GridSubgridContext? SubgridContext;
        internal double RelativeOffsetX;
        internal double RelativeOffsetY;
    }
}
