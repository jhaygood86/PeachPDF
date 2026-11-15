#nullable enable

using PeachPDF;
using PeachBrackets.Css;
using PeachDrawing.Core;
using PeachBrackets.Html.Entities;
using PeachBrackets.Html.Fragmentation;
using PeachBrackets.Html.Parse;
using PeachBrackets.Html.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PeachBrackets.Html.Dom
{
    /// <summary>
    /// <c>display: grid-lanes</c> / <c>inline-grid-lanes</c> (<see href="https://drafts.csswg.org/css-grid-3/">CSS Grid Layout Module Level 3</see>,
    /// formerly masonry): a grid in one axis and a stack in the other. The tracks of the <b>grid axis</b> are sized as a grid sizes
    /// them; each item is then dropped into the lane (or run of lanes, for a span) whose running end is nearest the start of the
    /// <b>stacking axis</b> - within <c>fit-tolerance</c> of the shortest - and the lane's running end moves past it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The grid axis is the columns unless <c>grid-auto-flow: row</c> asks for rows, or <c>normal</c> finds a row template and no
    /// column one. The container is laid out whole (<c>MonolithicContent.IsUnresumableVerticalGrid</c> keeps its parent from cutting it),
    /// so none of the grid's row relocation or item commit pass applies. Subgrids, vertical writing modes and
    /// <c>direction: rtl</c> are not mapped.
    /// </para>
    /// </remarks>
    internal sealed partial class CssLayoutEngineGrid
    {
        /// <summary>Whether <paramref name="box"/> is a grid-lanes container.</summary>
        internal static bool IsLanesContainer(CssBox box) =>
            box.Display.Value is DisplayMode.GridLanes or DisplayMode.InlineGridLanes;

        private bool IsLanes => IsLanesContainer(_gridBox);

        /// <summary>A percentage row gap against an indefinite height, which takes its final value from the height the lanes give the container.</summary>
        private bool _lanesRowGapResolvesLate;

        /// <summary><c>grid-lanes-direction: ... fill-reverse</c>: items stack from the end of the stacking axis.</summary>
        private bool LanesFillReverse => (_gridBox.GridLanesDirection ?? "").Contains("fill-reverse", StringComparison.OrdinalIgnoreCase);

        /// <summary><c>grid-lanes-direction: ... track-reverse</c>: the lanes run from the end of the grid axis, so the first lane is the last one.</summary>
        private bool LanesTrackReverse => (_gridBox.GridLanesDirection ?? "").Contains("track-reverse", StringComparison.OrdinalIgnoreCase);

        /// <summary>Dense packing: <c>grid-lanes-pack: dense</c>, or <c>dense</c> in <c>grid-auto-flow</c>.</summary>
        private bool LanesDense =>
            (_gridBox.GridLanesPack ?? "").Contains(Keywords.Dense, StringComparison.OrdinalIgnoreCase)
            || (_gridBox.GridAutoFlow ?? "").Contains(Keywords.Dense, StringComparison.OrdinalIgnoreCase);

        /// <summary>One item's lane choice: the first lane it covers and how many it spans (<see cref="Span"/>).</summary>
        private sealed class LaneItem
        {
            public required CssBox Box { get; init; }

            /// <summary>The first lane, or -1 for an item that is auto-placed in the grid axis.</summary>
            public int Start { get; set; } = -1;

            public int Span { get; set; } = 1;
        }

        /// <summary>The grid axis of a lanes container: rows when <c>grid-auto-flow: row</c>, or when <c>normal</c> meets a row template and no column one.</summary>
        private bool LanesUseRows(bool hasColumnTemplate, bool hasRowTemplate)
        {
            // grid-lanes-direction, where set, decides the axis; grid-auto-flow is what WebKit and css-grid-3 use instead.
            var direction = _gridBox.GridLanesDirection ?? Keywords.Normal;
            if (direction.IndexOf(Keywords.Row, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (direction.IndexOf(Keywords.Column, StringComparison.OrdinalIgnoreCase) >= 0) return false;

            var flow = _gridBox.GridAutoFlow ?? Keywords.Normal;
            if (flow.IndexOf(Keywords.Row, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (flow.IndexOf(Keywords.Column, StringComparison.OrdinalIgnoreCase) >= 0) return false;
            return !hasColumnTemplate && hasRowTemplate;
        }

        /// <summary>
        /// Resolves <c>fit-tolerance</c> (css-grid-3 §4.4) to a length: <c>normal</c> is <c>1em</c>, a percentage is of the grid-axis
        /// content box, and <c>infinite</c> is reported as <see cref="double.PositiveInfinity"/>.
        /// </summary>
        private double ResolveFitTolerance(double gridAxisContentSize)
        {
            var value = _gridBox.FitTolerance;
            if (string.IsNullOrWhiteSpace(value) || value.Isi(Keywords.Normal))
                return CssValueParser.ParseLength("1em", gridAxisContentSize, _gridBox);
            if (value.Isi(Keywords.Infinite))
                return double.PositiveInfinity;
            return Math.Max(0, CssValueParser.ParseLength(value, gridAxisContentSize, _gridBox));
        }

        /// <summary>
        /// Picks the first lane of an auto-placed item (css-grid-3 §4.4): among the start lines whose spanned lanes end no later than
        /// <paramref name="tolerance"/> past the shortest candidate, the first at or after <paramref name="cursor"/>, else the first.
        /// <c>infinite</c> tolerance is a round robin from the cursor.
        /// </summary>
        private static int ChooseLane(double[] running, int span, int cursor, double tolerance)
        {
            var starts = running.Length - span + 1;
            if (starts <= 1) return 0;

            if (double.IsPositiveInfinity(tolerance))
                return cursor < starts ? cursor : 0;

            var ends = new double[starts];
            var shortest = double.MaxValue;
            for (var i = 0; i < starts; i++)
            {
                var end = double.MinValue;
                for (var t = i; t < i + span; t++) end = Math.Max(end, running[t]);
                ends[i] = end;
                shortest = Math.Min(shortest, end);
            }

            var limit = shortest + tolerance + 0.001;
            for (var i = cursor; i < starts; i++)
                if (ends[i] <= limit) return i;
            for (var i = 0; i < Math.Min(cursor, starts); i++)
                if (ends[i] <= limit) return i;
            return 0;
        }

        /// <summary>
        /// Where the stacking range starts inside the container's content box, from <c>align-content</c> / <c>justify-content</c> along the stacking axis
        /// (css-grid-3 4.5): <c>start</c>, <c>center</c> and <c>end</c> move it; <c>flow-start</c> and <c>flow-end</c> follow the stacking direction; every
        /// other value, including <c>normal</c> and <c>stretch</c>, is <c>flow-start</c>. <c>safe</c> keeps an overflowing range at the start.
        /// </summary>
        private static double StackingRangeOffset(string value, double free, bool fillReverse)
        {
            var words = (value ?? "").ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var safe = words.Contains("safe");
            if (free < 0 && safe) return 0;

            return (words.Length == 0 ? Keywords.Normal : words[^1]) switch
            {
                "center" => free / 2,
                "end" or "flex-end" or "self-end" or "right" => free,
                "start" or "flex-start" or "self-start" or "left" => 0,
                "flow-end" => fillReverse ? 0 : free,
                _ => fillReverse ? free : 0
            };
        }

        /// <summary>Whether the stacking-axis content alignment names a physical position, so the last items must not stretch into the free space.</summary>
        private static bool PlacesRangeExplicitly(string value) =>
            (value ?? "").ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() is "center" or "end" or "flex-end"
                or "self-end" or "right" or "flow-end";

        /// <summary>
        /// The tracks a subgrid item adopts from this lanes container (css-grid-3 4.6): the lanes it spans in the grid axis, and in the stacking axis
        /// nothing - there it is a standalone grid. <paramref name="rows"/> is the lanes' rows when the rows are the lanes, and
        /// <paramref name="adopt"/> false asks the item to report its own row sizes instead.
        /// </summary>
        private static GridSubgridContext? LaneSubgridContext(LaneItem item, Track[]? columns, Track[]? rows, bool adopt,
            Dictionary<string, List<int>> colLineNames, Dictionary<string, List<int>> rowLineNames, double columnGap = 0, double rowGap = 0)
        {
            if (!IsSubgridItem(item.Box)) return null;

            var context = new GridSubgridContext { ParentColumnGap = columnGap, ParentRowGap = rowGap };
            if (columns is not null && ChildColSubgrid(item.Box))
            {
                context.ColumnSizes = SpannedSizes(columns, item.Start, item.Span);
                context.ColumnOffsets = SpannedOffsets(columns, item.Start, item.Span);
                context.ColumnLineNames = SpannedLineNames(colLineNames, item.Start, item.Span);
            }

            if (rows is not null && ChildRowSubgrid(item.Box))
            {
                context.RowCount = item.Span;
                context.RowLineNames = SpannedLineNames(rowLineNames, item.Start, item.Span);
                if (adopt)
                {
                    context.RowSizes = SpannedSizes(rows, item.Start, item.Span);
                    context.RowOffsets = SpannedOffsets(rows, item.Start, item.Span);
                }
            }

            return context;
        }

        /// <summary>Whether this lanes container is laid out against real fragmentainers (a page, a column) that content can be cut by.</summary>
        private bool LanesFragment()
        {
            var container = _gridBox.HtmlContainer;
            return !_vertical && container is { HasRealPageGrid: true, IsFragmenting: true }
                && _gridBox.DerivedStyle.ActualDisplay != Keywords.InlineGrid;
        }

        /// <summary>
        /// Moves items across fragmentainer boundaries lane by lane (css-grid-3 4.7, css-break-3). An item that may not be cut, or that follows a forced
        /// break, moves to the next fragmentainer; the items below it in its lanes move with it, and the other lanes stay where they are. An item the
        /// boundary cuts goes on in the next fragmentainer, so what follows it in its lanes starts below the part that continues there. Returns how far
        /// below the container's content origin the last item now ends.
        /// </summary>
        private double RelocateLanesAcrossFragmentainers(List<LaneItem> laneItems, int laneCount, double stackingGap)
        {
            var container = _gridBox.HtmlContainer!;
            var displacement = new double[laneCount];
            var floor = new double[laneCount];
            for (var t = 0; t < laneCount; t++) floor[t] = double.MinValue;

            var end = 0.0;
            var origin = BlockOrigin;
            var floorOwner = new CssBox?[laneCount];
            foreach (var item in laneItems.OrderBy(i => i.Box.Location.Y - i.Box.ActualMarginTop))
            {
                var box = item.Box;
                var state = LaneStates.GetOrCreateValue(box);
                state.Start = item.Start;
                state.Span = item.Span;
                state.Gap = stackingGap;
                state.FlooredBy = null;
                var naturalTop = box.Location.Y - box.ActualMarginTop;
                var outer = OuterBlockOf(box);

                var carried = 0.0;
                var lowest = double.MinValue;
                for (var t = item.Start; t < item.Start + item.Span; t++)
                {
                    carried = Math.Max(carried, displacement[t]);
                    lowest = Math.Max(lowest, floor[t]);
                }

                var top = Math.Max(naturalTop + carried, lowest);
                if (lowest > naturalTop + carried)
                {
                    for (var t = item.Start; t < item.Start + item.Span; t++)
                        if (floor[t] == lowest) state.FlooredBy = floorOwner[t];
                }
                var delta = LineRelocation.DeltaFor(container, box, top, top + outer, LineRelocation.ForcedBreakBefore(box),
                    LineRelocation.MayNotBeCutBox(box));
                top += delta;

                var shift = top - naturalTop;
                if (Math.Abs(shift) > 0.01) box.OffsetTop(shift);
                for (var t = item.Start; t < item.Start + item.Span; t++) displacement[t] = Math.Max(displacement[t], shift);

                var slot = container.SlotStartingAt(top);
                var bottom = top + outer;
                if (bottom - HtmlContainerInt.PageBoundaryEpsilon > container.PageBottomOf(slot) && outer <= container.PageBandHeightOf(slot + 1) * 4)
                {
                    // What spills over the boundary goes on at the top of the next fragmentainer, so the lanes' next item starts below it.
                    // A line is never split by the boundary: the one it falls through moves down whole, so the part that continues is up to a line
                    // taller than the overflow, and a line is allowed for.
                    var continuation = bottom - container.PageBottomOf(slot) + box.ActualLineHeight;
                    for (var t = item.Start; t < item.Start + item.Span; t++)
                    {
                        floor[t] = Math.Max(floor[t], container.PageTopOf(slot + 1) + continuation + stackingGap);
                        floorOwner[t] = box;
                    }
                    bottom = container.PageTopOf(slot + 1) + continuation;
                }

                end = Math.Max(end, bottom - origin);
            }

            return end;
        }

        /// <summary>Where an item sits in the lanes, kept for the commit pass: its lanes, the gap below it, and the cut item whose continuation it was placed under.</summary>
        private sealed class LaneState
        {
            public int Start;
            public int Span;
            public double Gap;
            public CssBox? FlooredBy;
        }

        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<CssBox, LaneState> LaneStates = new();

        /// <summary>
        /// After the items of group <paramref name="finishedRow"/> have been committed their true extent is known. An item placed below a cut one was placed
        /// under an estimate of where the cut item ends (it cannot be known before its content has been laid out against the next fragmentainer); it is moved
        /// to exactly below it, and the items below it in its lanes move with it.
        /// </summary>
        private void SettleLanes(IReadOnlyList<IReadOnlyList<CssBox>> rows, int finishedRow)
        {
            var later = rows.Skip(finishedRow + 1).SelectMany(r => r).Where(b => LaneStates.TryGetValue(b, out _))
                .OrderBy(b => b.Location.Y - b.ActualMarginTop).ToList();
            if (later.Count == 0) return;

            var shifts = new Dictionary<int, double>();
            var moved = false;
            foreach (var box in later)
            {
                LaneStates.TryGetValue(box, out var state);
                var top = box.Location.Y - box.ActualMarginTop;

                double? shift = null;
                for (var t = state!.Start; t < state.Start + state.Span; t++)
                    if (shifts.TryGetValue(t, out var carried)) shift = Math.Max(shift ?? carried, carried);

                if (state.FlooredBy is { } cut && rows[finishedRow].Contains(cut))
                {
                    // The item's own bottom is the height it was pinned to; the content a boundary pushed down reaches past it.
                    var reach = CssBox.GetMaximumBottom(cut, cut.Location.Y) + cut.ActualPaddingBottom + cut.ActualBorderBottomWidth;
                    shift = Math.Max(cut.ActualBottom, reach) + cut.ActualMarginBottom + state.Gap - top;
                    state.FlooredBy = null;
                }

                if (shift is not { } applied) continue;

                if (Math.Abs(applied) > 0.01)
                {
                    box.OffsetTop(applied);
                    moved = true;
                }

                for (var t = state.Start; t < state.Start + state.Span; t++) shifts[t] = applied;
            }

            // The container is as tall as the lowest item once the items have moved.
            if (moved && !CssValueParser.IsValidLength(_gridBox.Height))
            {
                var bottom = rows.SelectMany(r => r).Where(b => LaneStates.TryGetValue(b, out _)).Max(b => b.ActualBottom + b.ActualMarginBottom);
                _gridBox.ActualBottom = bottom + _gridBox.ActualPaddingBottom + _gridBox.ActualBorderBottomWidth;
            }
        }

        /// <summary>
        /// Commits the items' content for real, a fragmentainer's worth at a time, so what a boundary cuts continues in the next one. Items that begin
        /// in the same fragmentainer are one group: they are parallel flows, and one stopping does not stop the others.
        /// </summary>
        private async ValueTask CommitLanes(Canvas g, List<LaneItem> laneItems, IReadOnlyDictionary<CssBox, GridSubgridContext> contexts)
        {
            var container = _gridBox.HtmlContainer;
            if (container is null) return;

            var groups = laneItems
                .GroupBy(i => container.SlotStartingAt(i.Box.Location.Y - i.Box.ActualMarginTop + HtmlContainerInt.PageBoundaryEpsilon))
                .OrderBy(group => group.Key)
                .Select(group => (IReadOnlyList<CssBox>)[.. group.Select(i => i.Box)])
                .ToList();

            await CommitItemContent(g, groups, 0, null, null, contexts, _gridBox.Location, SettleLanes);
        }

        /// <summary>An item's outer extent along the inline axis (its margin box's width, or height in a vertical writing mode).</summary>
        private double OuterInlineOf(CssBox box) => _vertical
            ? box.ActualBoxSizingHeight + MarginsInline(box)
            : box.ActualBoxSizingWidth + box.ActualMarginLeft + box.ActualMarginRight;

        /// <summary>An item's outer extent along the block axis (its margin box's height, or width in a vertical writing mode).</summary>
        private double OuterBlockOf(CssBox box) => _vertical
            ? box.ActualBoxSizingWidth + MarginsBlock(box)
            : box.ActualBoxSizingHeight + box.ActualMarginTop + box.ActualMarginBottom;

        /// <summary>Moves a laid-out item <paramref name="inline"/> along the inline axis and <paramref name="block"/> along the block axis.</summary>
        private void Shift(CssBox box, double inline, double block)
        {
            double dx, dy;
            if (_vertical)
            {
                var origin = _frame.ToPhysical(new Rect(0, 0, 1, 1));
                var moved = _frame.ToPhysical(new Rect(inline, block, 1, 1));
                dx = moved.X - origin.X;
                dy = moved.Y - origin.Y;
            }
            else
            {
                dx = inline;
                dy = block;
            }

            if (Math.Abs(dx) > 0.01) box.OffsetLeft(dx);
            if (Math.Abs(dy) > 0.01) box.OffsetTop(dy);
        }

        /// <summary>Sizes the container along its block axis to <paramref name="blockExtent"/> when that size is auto.</summary>
        private void SizeLanesBlock(bool hasDefiniteBlock, double blockExtent)
        {
            if (hasDefiniteBlock) return;

            if (_vertical)
                CssLayoutEngine.ShrinkAutoWidthTo(_gridBox, _frame, blockExtent);
            else
                _gridBox.ActualBottom = _gridBox.ClientTop + blockExtent + _gridBox.ActualPaddingBottom + _gridBox.ActualBorderBottomWidth;
        }

        private async ValueTask LayoutLanes(Canvas g)
        {
            // direction: rtl mirrors the layout about the content box (PlaceItemInCell reflects each item), so the lanes are laid out as for ltr.
            _rtl = !_vertical && _gridBox.Direction.Value == DirectionMode.Rtl;
            var containerWidth = await CssLayoutEngine.GetBoxWidth(g, _gridBox);
            _gridBox.ActualRight = _gridBox.Location.X + containerWidth + _gridBox.ActualBoxSizeIncludedWidth;
            _gridBox.ActualBottom = _gridBox.Location.Y;
            _mirrorRight = _gridBox.ClientRight;

            var resolvedHeight = CssValueParser.IsValidLength(_gridBox.Height)
                                 || CssLayoutEngine.TryGetAspectRatioHeight(_gridBox, out _)
                ? CssLayoutEngine.GetBoxHeight(_gridBox)
                : null;
            var hasDefiniteHeight = resolvedHeight is not null;
            if (hasDefiniteHeight)
                _gridBox.ActualBottom = _gridBox.Location.Y + resolvedHeight!.Value;

            foreach (var runningChild in _gridBox.Boxes.Where(b => b.IsRunningPositioned))
                runningChild.RegisterAsRunningElement(_gridBox);

            var items = _gridBox.Boxes.Where(DomUtils.GeneratesFlexOrGridItem).ToList();
            if (items.Count == 0 && !_gridBox.Boxes.Any(static b => b.Position.Value is PositionMode.Absolute && !b.IsRunningPositioned))
            {
                if (!hasDefiniteHeight)
                    _gridBox.ActualBottom = _gridBox.Location.Y + _gridBox.ActualBoxSizeIncludedHeight;
                return;
            }

            foreach (var item in items)
                ItemContentCommit.UnpinIfPinned(item);

            // The inline axis is the container's height in a vertical writing mode, and its size is what makes the block axis (the width) definite.
            var hasDefiniteBlock = _vertical ? CssValueParser.IsValidLength(_gridBox.Width) : hasDefiniteHeight;
            var inlineIndefinite = _vertical && !hasDefiniteHeight;
            var contentWidth = _vertical
                ? hasDefiniteHeight ? _gridBox.ClientBottom - _gridBox.ClientTop : 0
                : _gridBox.ClientRight - _gridBox.ClientLeft;
            if (_vertical) _frame = WritingModeFrame.For(_gridBox);
            var columnGap = ParseGap(_gridBox.FlexColumnGap, contentWidth);

            // A percentage row gap is of the container's height: against an indefinite one the percentage counts as 0 while the lanes are sized and the
            // gap takes its final value from the height that results (css-align-3 8.1).
            var rowGapIsPercentage = _gridBox.FlexRowGap.Value is { IsValue: true, Value: { } rowGapLength }
                && (rowGapLength.IsCalc ? rowGapLength.CalcText!.Contains('%') : rowGapLength.Length!.Value.Type == Length.Unit.Percent);
            var rowGap = ParseGap(_gridBox.FlexRowGap,
                _vertical || !rowGapIsPercentage ? contentWidth : hasDefiniteHeight ? _gridBox.ClientBottom - _gridBox.ClientTop : 0);
            _lanesRowGapResolvesLate = !_vertical && rowGapIsPercentage && !hasDefiniteHeight;
            var colTemplate = _gridBox.GridTemplateColumns.Value;
            var rowTemplate = _gridBox.GridTemplateRows.Value;
            var rowsAreLanes = LanesUseRows(
                colTemplate is { } c && (c.Tracks.Count > 0 || c.AutoRepeat != GridAutoRepeatKind.None || c.IsSubgrid),
                rowTemplate is { } r && (r.Tracks.Count > 0 || r.AutoRepeat != GridAutoRepeatKind.None || r.IsSubgrid));

            // A repeat() whose tracks have no definite size is counted from a hypothetical sizing of the lanes (css-grid-3 3.1.1).
            _laneRepeatCount = null;
            if (!_vertical && !inlineIndefinite)
            {
                _laneRepeatCount = rowsAreLanes
                    ? await IntrinsicRepeatCount(g, items, rowTemplate, true, hasDefiniteBlock ? BlockContentSize : 0, rowGap, contentWidth)
                    : await IntrinsicRepeatCount(g, items, colTemplate, false, contentWidth, columnGap, contentWidth);
            }

            // An auto-repeat's resolved length places the lines named after it.
            var colLineNames = BuildLineNames(colTemplate, colTemplate is { AutoRepeat: not GridAutoRepeatKind.None }
                ? ResolveColumnTemplate(colTemplate, contentWidth, columnGap, rowsAreLanes ? null : _laneRepeatCount).Defs.Count : -1);
            var rowLineNames = BuildLineNames(rowTemplate, rowTemplate is { AutoRepeat: not GridAutoRepeatKind.None }
                ? ResolveColumnTemplate(rowTemplate, hasDefiniteBlock ? BlockContentSize : 0, rowGap, rowsAreLanes ? _laneRepeatCount : null).Defs.Count : -1);

            // A lanes container that is a subgrid also has the parent's names for the lines it spans (css-grid-2 9.2).
            var subgrid = _gridBox.SubgridContext;
            if (colTemplate?.IsSubgrid == true && subgrid?.ColumnLineNames is { } inheritedColumns) AddInheritedNames(colLineNames, inheritedColumns);
            if (rowTemplate?.IsSubgrid == true && subgrid?.RowLineNames is { } inheritedRows) AddInheritedNames(rowLineNames, inheritedRows);

            if (rowsAreLanes)
                await LayoutRowLanes(g, items, hasDefiniteBlock, inlineIndefinite, contentWidth, columnGap, rowGap, rowTemplate, colLineNames, rowLineNames);
            else
                await LayoutColumnLanes(g, items, hasDefiniteBlock, inlineIndefinite, contentWidth, columnGap, rowGap, colTemplate, colLineNames, rowLineNames);
        }

        /// <summary>The repetition count of the lanes template's <c>repeat(auto-fill|auto-fit, ...)</c> when set from a hypothetical sizing, else null.</summary>
        private int? _laneRepeatCount;

        /// <summary>
        /// The number of repetitions of an auto repeat whose tracks have no definite size (css-grid-3 3.1.1). The track list is expanded enough
        /// times to hold every placement of the widest item, every item is treated as auto-placed and no track collapses, the lanes are sized, and
        /// the largest track of each entry of the repeat (by index) is that entry's size, which then says how many repetitions fill the container.
        /// Returns null when every entry has a definite size, which the ordinary count handles.
        /// </summary>
        private async ValueTask<int?> IntrinsicRepeatCount(Canvas g, List<CssBox> items, GridTemplate? template, bool rowsAreLanes,
            double axisSize, double gap, double contentWidth)
        {
            if (template is not { AutoRepeat: not GridAutoRepeatKind.None, AutoRepeatTracks: { Count: > 0 } repeated })
                return null;
            if (repeated.All(d => FloorSize(d) > 0))
                return null;
            if (axisSize <= 0)
                return 1;

            var entries = repeated.Count;
            var before = template.AutoRepeatInsertIndex;
            var empty = new Dictionary<string, List<int>>();
            var laneItems = new List<LaneItem>();
            var widest = 1;
            foreach (var box in items)
            {
                var (_, span) = rowsAreLanes
                    ? ResolveAxis(box.GridRowStart, box.GridRowEnd, 0, empty)
                    : ResolveAxis(box.GridColumnStart, box.GridColumnEnd, 0, empty);
                span = Math.Max(1, span);
                widest = Math.Max(widest, span);
                laneItems.Add(new LaneItem { Box = box, Start = -1, Span = span });
            }

            var copies = Math.Max(1, (int)Math.Floor(2 + (widest - 2.0) / entries));
            var defs = new List<GridTrackSize>(template.Tracks.Take(before));
            for (var c = 0; c < copies; c++) defs.AddRange(repeated);
            defs.AddRange(template.Tracks.Skip(before));
            var count = defs.Count;
            foreach (var item in laneItems) item.Span = Math.Min(item.Span, count);

            double[] min, max;
            if (rowsAreLanes)
            {
                // A row's contribution is the height an item has at its own width, which fits its content up to the container's.
                min = new double[count];
                foreach (var item in laneItems)
                {
                    await PlaceItemInCell(g, item.Box, InlineOrigin, BlockOrigin, contentWidth, 0, Keywords.Start, Keywords.Start);
                    var height = OuterBlockOf(item.Box);
                    var share = item.Span > 1 ? Math.Max(0, height - gap * (item.Span - 1)) / item.Span : height;
                    for (var r = 0; r < count; r++) min[r] = Math.Max(min[r], share);
                }

                max = min;
            }
            else
            {
                (min, max) = await MeasureLaneIntrinsics(g, laneItems, defs, count, contentWidth, gap, empty);
            }

            var sizes = SizeColumnTracks(defs, axisSize, gap, new bool[count], min, max, false);
            double fixedTotal = 0;
            for (var i = 0; i < before; i++) fixedTotal += sizes[i].Size;
            for (var i = before + copies * entries; i < count; i++) fixedTotal += sizes[i].Size;

            double repeatedTotal = 0;
            for (var e = 0; e < entries; e++)
            {
                var entry = FloorSize(repeated[e]);
                if (entry <= 0)
                    for (var c = 0; c < copies; c++) entry = Math.Max(entry, sizes[before + c * entries + e].Size);
                repeatedTotal += entry;
            }

            repeatedTotal += gap * (entries - 1);
            if (repeatedTotal <= 0) return 1;
            return Math.Max(1, (int)Math.Floor((axisSize - fixedTotal + gap) / (repeatedTotal + gap)));
        }

        /// <summary>Orders the items by <c>order</c> (stable) and resolves each one's start lane and span in the grid axis.</summary>
        private static List<LaneItem> ResolveLaneItems(List<CssBox> items, bool rowsAreLanes, int explicitCount,
            IReadOnlyDictionary<string, List<int>> lineNames)
        {
            var ordered = items.OrderBy(box => int.TryParse(box.Order, out var order) ? order : 0);
            var result = new List<LaneItem>(items.Count);
            foreach (var box in ordered)
            {
                var (start, span) = rowsAreLanes
                    ? ResolveAxis(box.GridRowStart, box.GridRowEnd, explicitCount, lineNames)
                    : ResolveAxis(box.GridColumnStart, box.GridColumnEnd, explicitCount, lineNames);
                result.Add(new LaneItem { Box = box, Start = start, Span = Math.Max(1, span) });
            }
            return result;
        }

        /// <summary>Drops the auto items' tracks that no item can reach: an <c>auto-fit</c> repetition keeps the lanes the items occupy and the first few more.</summary>
        private static void CollapseAutoFit(bool[] collapsed, (int Start, int Length)? autoFitRange, List<LaneItem> laneItems)
        {
            if (autoFitRange is not var (rangeStart, rangeLength) || rangeLength <= 0) return;

            var occupied = new bool[collapsed.Length];
            var autoSpan = 0;
            foreach (var item in laneItems)
            {
                if (item.Start < 0) { autoSpan += item.Span; continue; }
                for (var t = item.Start; t < item.Start + item.Span && t < occupied.Length; t++) occupied[t] = true;
            }

            var spare = autoSpan;
            for (var t = rangeStart; t < rangeStart + rangeLength && t < collapsed.Length; t++)
            {
                if (occupied[t]) continue;
                if (spare > 0) { spare--; continue; }
                collapsed[t] = true;
            }
        }

        /// <summary>Collapses the lanes of an auto-fit repetition that no placed item covers; false when none were left to collapse.</summary>
        private static bool CollapseUnused(bool[] collapsed, (int Start, int Length) range, List<LaneItem> laneItems)
        {
            var occupied = new bool[collapsed.Length];
            foreach (var item in laneItems)
                for (var t = Math.Max(0, item.Start); t < item.Start + item.Span && t < occupied.Length; t++) occupied[t] = true;

            var changed = false;
            for (var t = range.Start; t < range.Start + range.Length && t < collapsed.Length; t++)
            {
                if (occupied[t] || collapsed[t]) continue;
                collapsed[t] = true;
                changed = true;
            }

            return changed;
        }

        /// <summary>The free stretches of one lane that an earlier item's start left above the running position: <c>[Start, End)</c>.</summary>
        private readonly record struct LaneGap(double Start, double End);

        /// <summary>
        /// Walks the items in order, giving each its lane and handing <paramref name="placeItem"/> the stacking-axis offset it starts at and taking
        /// back the item's outer size there. With dense packing an item may afterwards move up into a stretch an earlier item skipped (css-grid-3
        /// 4.3): one in lanes whose spanned size equals the size of the lanes it was laid out in, so it never has to be laid out twice. The
        /// cursor and the running positions are not touched by such a move.
        /// </summary>
        private async ValueTask StackLanes(List<LaneItem> laneItems, int laneCount, double tolerance, double stackingGap, bool dense,
            Func<int, int, double> spanSize, Func<LaneItem, double, ValueTask<double>> placeItem)
        {
            var running = new double[laneCount];
            var gaps = dense ? Enumerable.Range(0, laneCount).Select(_ => new List<LaneGap>()).ToArray() : [];
            var cursor = 0;
            foreach (var item in laneItems)
            {
                item.Span = Math.Min(item.Span, laneCount);
                var definite = item.Start >= 0;
                var previousCursor = cursor;
                if (!definite)
                {
                    item.Start = ChooseLane(running, item.Span, cursor, tolerance);
                    cursor = item.Start + item.Span;
                    if (cursor >= laneCount) cursor = 0;
                }
                item.Start = Math.Min(item.Start, laneCount - item.Span);

                var stackStart = 0.0;
                for (var t = item.Start; t < item.Start + item.Span; t++) stackStart = Math.Max(stackStart, running[t]);

                var outer = await placeItem(item, stackStart);
                var occupied = Math.Max(0, outer) + stackingGap;

                if (dense && !definite && TryBackfill(gaps, item, laneCount, stackStart, occupied, tolerance, spanSize, out var lane, out var top))
                {
                    item.Start = lane;
                    await placeItem(item, top);
                    cursor = previousCursor;
                    continue;
                }

                for (var t = item.Start; t < item.Start + item.Span; t++)
                {
                    if (dense && stackStart > running[t] + 0.001) gaps[t].Add(new LaneGap(running[t], stackStart));
                    running[t] = stackStart + occupied;
                }
            }
        }

        /// <summary>
        /// Finds the highest stretch, common to the lanes of some start line whose spanned size matches the item's, that is tall enough for an item
        /// occupying <paramref name="occupied"/> (its outer size and the gap after it) and higher than <paramref name="stackStart"/>. Of the starts within
        /// <paramref name="tolerance"/> of the highest, the first wins. The gaps it uses are cut out of the lanes.
        /// </summary>
        private static bool TryBackfill(List<LaneGap>[] gaps, LaneItem item, int laneCount, double stackStart, double occupied, double tolerance,
            Func<int, int, double> spanSize, out int lane, out double top)
        {
            lane = item.Start;
            top = stackStart;
            var wanted = spanSize(item.Start, item.Span);
            var bestTop = double.MaxValue;
            var candidates = new List<(int Lane, double Top)>();
            for (var start = 0; start + item.Span <= laneCount; start++)
            {
                if (Math.Abs(spanSize(start, item.Span) - wanted) > 0.01) continue;

                var common = gaps[start];
                for (var t = start + 1; t < start + item.Span && common.Count > 0; t++)
                    common = IntersectGaps(common, gaps[t]);

                foreach (var gap in common.OrderBy(g => g.Start))
                {
                    if (gap.End - gap.Start + 0.001 < occupied) continue;
                    if (gap.Start >= stackStart - 0.001) break;
                    candidates.Add((start, gap.Start));
                    bestTop = Math.Min(bestTop, gap.Start);
                    break;
                }
            }

            if (candidates.Count == 0) return false;

            var reach = double.IsPositiveInfinity(tolerance) ? double.MaxValue / 2 : tolerance;
            var chosen = candidates.First(c => c.Top <= bestTop + reach + 0.001);
            lane = chosen.Lane;
            top = chosen.Top;
            for (var t = lane; t < lane + item.Span; t++)
                CutGap(gaps[t], top, top + occupied);
            return true;
        }

        private static List<LaneGap> IntersectGaps(List<LaneGap> a, List<LaneGap> b)
        {
            var result = new List<LaneGap>();
            foreach (var x in a)
                foreach (var y in b)
                {
                    var start = Math.Max(x.Start, y.Start);
                    var end = Math.Min(x.End, y.End);
                    if (end - start > 0.001) result.Add(new LaneGap(start, end));
                }
            return result;
        }

        private static void CutGap(List<LaneGap> gaps, double start, double end)
        {
            for (var i = 0; i < gaps.Count; i++)
            {
                var gap = gaps[i];
                if (start < gap.Start - 0.001 || end > gap.End + 0.001) continue;
                gaps.RemoveAt(i);
                if (start - gap.Start > 0.001) gaps.Add(new LaneGap(gap.Start, start));
                if (gap.End - end > 0.001) gaps.Add(new LaneGap(end, gap.End));
                return;
            }
        }

        /// <summary>
        /// The min-content and max-content contributions of the items to the lanes (css-grid-3 §4.4.1). An auto-placed item may land in any
        /// lane it fits, so it contributes to every one of them and the lanes stay alike; a definite one only to the lanes it covers. A span is
        /// shared evenly across the lanes it covers, once - not once per start line, which would weigh the middle lanes twice.
        /// </summary>
        private async ValueTask<(double[] Min, double[] Max)> MeasureLaneIntrinsics(Canvas g, List<LaneItem> laneItems,
            IReadOnlyList<GridTrackSize> colDefs, int colCount, double contentWidth, double gap, Dictionary<string, List<int>> colLineNames)
        {
            var min = new double[colCount];
            var max = new double[colCount];
            if (!colDefs.Any(d => IsIntrinsic(d) || d.Kind == GridTrackKind.Flex)) return (min, max);

            foreach (var item in laneItems)
            {
                var span = Math.Min(item.Span, colCount);
                var first = item.Start >= 0 ? item.Start : 0;
                var last = item.Start >= 0 ? Math.Min(item.Start + span, colCount) : colCount;
                var covered = Enumerable.Range(first, last - first)
                    .Where(c => IsIntrinsic(colDefs[c]) || colDefs[c].Kind == GridTrackKind.Flex).ToList();
                if (covered.Count == 0) continue;

                // A column subgrid's own items size the lanes it spans (css-grid-2 9.3), not the subgrid as one box. An auto-placed one may land on any
                // start line, so its items contribute at each of them.
                if (!_vertical && !_rtl && IsSubgridItem(item.Box) && ChildColSubgrid(item.Box))
                {
                    var contributed = false;
                    for (var s = first; s + span <= (item.Start >= 0 ? first + span : colCount); s++)
                    {
                        var contributions = new List<SubgridContribution>();
                        if (!await new CssLayoutEngineGrid(item.Box).CollectSubgridColumnContributions(
                                g, span, SpannedLineNames(colLineNames, s, span), s, contributions, gap))
                            break;

                        contributed = true;
                        foreach (var c in contributions.Where(c => c.Start < colCount))
                        {
                            var lastLane = Math.Min(c.Start + c.Span, colCount);
                            var intrinsic = Enumerable.Range(c.Start, lastLane - c.Start)
                                .Where(i => IsIntrinsic(colDefs[i]) || colDefs[i].Kind == GridTrackKind.Flex).ToList();
                            if (intrinsic.Count == 0) continue;

                            var share = c.Span > 1 ? 1.0 / c.Span : 1.0;
                            foreach (var lane in intrinsic)
                            {
                                var minimum = c.Span > 1 ? Math.Max(0, c.Min + c.Edge - gap * (c.Span - 1)) * share
                                    : colDefs[lane].Kind == GridTrackKind.Flex ? AutoMinimumOf(c.Box, c.Min, contentWidth) + c.Edge : c.Min + c.Edge;
                                min[lane] = Math.Max(min[lane], minimum);
                                max[lane] = Math.Max(max[lane], c.Span > 1 ? Math.Max(0, c.Max + c.Edge - gap * (c.Span - 1)) * share : c.Max + c.Edge);
                            }
                        }
                    }

                    if (contributed) continue;
                }

                var (mn, mx) = _vertical ? await MeasureInlineContributionVertical(g, item.Box) : await MeasureItemContribution(g, item.Box, percentageIsAuto: true);
                // An auto-placed item that spans lanes may start at any of them. At each start it asks the intrinsic lanes it covers for what the
                // lanes with a fixed size do not already give it, so a lane next to a fixed one is asked for more than one between two intrinsic ones.
                if (item.Start < 0 && span > 1)
                {
                    for (var s = 0; s + span <= colCount; s++)
                    {
                        var range = Enumerable.Range(s, span).ToList();
                        var intrinsic = range.Where(i => IsIntrinsic(colDefs[i]) || colDefs[i].Kind == GridTrackKind.Flex).ToList();
                        if (intrinsic.Count == 0) continue;

                        var fixedSize = range.Except(intrinsic).Sum(i => ResolveFixedLength(colDefs[i], contentWidth));
                        var roomMin = Math.Max(0, mn - gap * (span - 1) - fixedSize) / intrinsic.Count;
                        var roomMax = Math.Max(0, mx - gap * (span - 1) - fixedSize) / intrinsic.Count;
                        foreach (var lane in intrinsic)
                        {
                            min[lane] = Math.Max(min[lane], roomMin);
                            max[lane] = Math.Max(max[lane], roomMax);
                        }
                    }

                    continue;
                }

                var shareOf = span > 1 ? Math.Max(0, 1.0 / span) : 1.0;
                foreach (var c in covered)
                {
                    var minimum = span > 1 ? Math.Max(0, mn - gap * (span - 1)) * shareOf : mn;
                    if (span == 1 && HasAutoMinimum(colDefs[c])) minimum = _vertical ? AutoMinimumOfVertical(item.Box, mn, contentWidth) : AutoMinimumOf(item.Box, mn, contentWidth);
                    min[c] = Math.Max(min[c], minimum);
                    max[c] = Math.Max(max[c], span > 1 ? Math.Max(0, mx - gap * (span - 1)) * shareOf : mx);
                }
            }
            return (min, max);
        }

        // ─── Columns are the lanes; items stack down the block axis ─────────────────

        private async ValueTask LayoutColumnLanes(Canvas g, List<CssBox> items, bool hasDefiniteHeight, bool inlineIndefinite, double contentWidth,
            double columnGap, double rowGap, GridTemplate? colTemplate,
            Dictionary<string, List<int>> colLineNames, Dictionary<string, List<int>> rowLineNames)
        {
            var subgrid = _gridBox.SubgridContext;
            var colSubgrid = colTemplate?.IsSubgrid == true && subgrid?.ColumnSizes is not null;
            if (colSubgrid && !_vertical && !_rtl)
            {
                _colStartInset = _gridBox.ActualMarginLeft + _gridBox.ActualBorderLeftWidth + _gridBox.ActualPaddingLeft;
                _colEndInset = _gridBox.ActualMarginRight + _gridBox.ActualBorderRightWidth + _gridBox.ActualPaddingRight;
            }

            (List<GridTrackSize> explicitColDefs, (int Start, int Length)? autoFitRange) = colSubgrid && subgrid?.ColumnSizes is { } adoptedColumnSizes
                ? ([.. Enumerable.Repeat(GridTrackSize.Auto, adoptedColumnSizes.Count)], null)
                : ResolveColumnTemplate(colTemplate, contentWidth, columnGap, _laneRepeatCount);
            var autoColDefs = ExpandTrackSizes(_gridBox.GridAutoColumns);
            var fixedCount = Math.Max(1, explicitColDefs.Count);

            var laneItems = ResolveLaneItems(items, rowsAreLanes: false, fixedCount, colLineNames);
            var colCount = fixedCount;
            foreach (var item in laneItems)
            {
                colCount = Math.Max(colCount, item.Span);
                if (item.Start >= 0) colCount = Math.Max(colCount, item.Start + item.Span);
            }

            var colDefs = BuildTrackDefs(explicitColDefs, autoColDefs, colCount);
            var collapsed = new bool[colCount];
            CollapseAutoFit(collapsed, autoFitRange, laneItems);

            var (colMin, colMax) = await MeasureLaneIntrinsics(g, laneItems, colDefs, colCount, contentWidth, columnGap, colLineNames);
            var stretchColumns = IsStretch(_gridBox.JustifyContent.ToString());

            // A vertical container with an auto height is as long as its lanes' max-content sizes (css-grid-2 11.1 with an indefinite space).
            if (inlineIndefinite)
                contentWidth = MaxContentInlineSize(colDefs, colMax, collapsed, columnGap);

            var trackReverse = LanesTrackReverse;
            Track[] columns = [];

            void BuildColumns()
            {
                if (colSubgrid && subgrid is { ColumnSizes: { } adoptedSizes, ColumnOffsets: { } adoptedOffsets })
                {
                    columns = BuildAdoptedTracks(adoptedSizes, adoptedOffsets, InlineOrigin - _colStartInset, colCount);
                }
                else
                {
                    columns = SizeColumnTracks(colDefs, contentWidth, columnGap, collapsed, colMin, colMax, stretchColumns);
                    PositionTracks(columns, InlineOrigin, contentWidth, columnGap, LogicalizePhysicalKeyword(_gridBox.JustifyContent.ToString()));
                }

                if (inlineIndefinite)
                {
                    _gridBox.ActualBottom = _gridBox.ClientTop + columns.Sum(c => c.Size) + (colCount > 1 ? columnGap * (colCount - 1) : 0)
                        + _gridBox.ActualPaddingBottom + _gridBox.ActualBorderBottomWidth;
                    _frame = WritingModeFrame.For(_gridBox);
                }

                // track-reverse runs the lanes from the end of the grid axis: the first lane is the last one on the page.
                if (trackReverse)
                    foreach (var column in columns)
                        column.Position = 2 * InlineOrigin + contentWidth - column.Position - column.Size;
            }

            BuildColumns();

            // justify-self: baseline puts an item at its start (or end) edge and lines the lane's group up afterwards (css-align-3 9.3).
            var inlineBaselines = new Dictionary<CssBox, (Placement Placement, bool Last)>();
            string JustifyOf(LaneItem item)
            {
                var justify = LogicalizePhysicalKeyword(ResolveSelfAlignment(item.Box.JustifySelf.ToString(), _gridBox.JustifyItems.ToString()));
                if (item.Span != 1 || !IsBaselineAlign(justify)) return justify;

                var last = justify.IndexOf("last", StringComparison.OrdinalIgnoreCase) >= 0;
                inlineBaselines[item.Box] = (new Placement { Box = item.Box, ColStart = item.Start }, last);
                return ColumnBaselineFallback(item.Box, last);
            }

            // The first and last lanes of a subgrid start inside its own margin, border and padding (css-grid-2 9.2).
            double CellX(LaneItem item) => columns[trackReverse ? item.Start + item.Span - 1 : item.Start].Position
                + (item.Start == 0 ? _colStartInset : 0);

            double CellWidthOf(LaneItem item) => ColumnSpanWidth(columns, item.Start, item.Span, columnGap)
                - (item.Start == 0 ? _colStartInset : 0) - (item.Start + item.Span >= columns.Length ? _colEndInset : 0);

            async ValueTask PlaceInLanes(LaneItem item, double top, double room, string justify, string align)
            {
                item.Box.SubgridContext = LaneSubgridContext(item, columns, null, true, colLineNames, rowLineNames, columnGap, rowGap);
                try
                {
                    await PlaceItemInCell(g, item.Box, CellX(item), BlockOrigin + top, CellWidthOf(item), room, justify, align);
                }
                finally
                {
                    item.Box.SubgridContext = null;
                }
            }

            var tolerance = ResolveFitTolerance(contentWidth);
            var origin = BlockOrigin;
            var stackEnd = 0.0;
            var tops = new Dictionary<CssBox, double>();
            var outers = new Dictionary<CssBox, double>();

            var requestedStarts = laneItems.Select(i => i.Start).ToList();

            async ValueTask Stack() => await StackLanes(laneItems, colCount, tolerance, rowGap, LanesDense,
                (start, span) => ColumnSpanWidth(columns, start, span, columnGap),
                async (item, stackStart) =>
                {
                    var justify = JustifyOf(item);

                    // The stacking axis never stretches an item: it is as tall as its content.
                    await PlaceInLanes(item, stackStart, 0, justify, Keywords.Start);
                    var outer = OuterBlockOf(item.Box);
                    tops[item.Box] = stackStart;
                    outers[item.Box] = outer;
                    return outer;
                });

            await Stack();

            // An auto-fit lane that no item ended up in collapses (css-grid-2 7.2.3.2), which widens the lanes left: they are sized and stacked again.
            if (!colSubgrid && autoFitRange is { } fit && CollapseUnused(collapsed, fit, laneItems))
            {
                for (var i = 0; i < laneItems.Count; i++) laneItems[i].Start = requestedStarts[i];
                tops.Clear();
                outers.Clear();
                inlineBaselines.Clear();
                BuildColumns();
                await Stack();
            }

            // An item dense packing moved up was first laid at its normal place, so the extent is read from where each one ended up.
            foreach (var item in laneItems)
                stackEnd = Math.Max(stackEnd, tops[item.Box] + outers[item.Box]);

            // Page breaks come first: what an item is aligned in depends on where the boundaries leave it and what follows it.
            var fragmenting = LanesFragment();
            var pages = _gridBox.HtmlContainer;
            if (fragmenting)
            {
                stackEnd = Math.Max(stackEnd, RelocateLanesAcrossFragmentainers(laneItems, colCount, rowGap));
                foreach (var item in laneItems)
                    tops[item.Box] = item.Box.Location.Y - item.Box.ActualMarginTop - origin;
            }

            // Reversing the stack, or positioning its range in the container, means nothing once the stack runs over a page boundary.
            var onOnePage = !fragmenting || pages!.SlotEndingAt(origin + stackEnd) == pages.SlotStartingAt(origin);
            var fillReverse = LanesFillReverse && onOnePage;

            // An item is aligned in the room from its start to where what follows it in its lane starts (less the gap), or, for the last one, to the
            // end of the stacking range - the container's content box when its height is definite. Stretch fills that room; center and end move
            // the item in it. It matters only where an earlier lane pushed the next item lower than this one ends.
            if (!fillReverse)
            {
                var extent = hasDefiniteHeight && !PlacesRangeExplicitly(_gridBox.AlignContent.ToString()) ? BlockContentSize : stackEnd;
                foreach (var item in laneItems)
                {
                    var align = ResolveSelfAlignment(item.Box.AlignSelf.ToString(), _gridBox.AlignItems.ToString());
                    if (align.Isi(Keywords.Start) || align.Isi(Keywords.FlexStart) || align.Isi("self-start") || align.Isi(Keywords.Normal)) continue;

                    var itemStart = tops[item.Box];
                    var end = extent;
                    foreach (var other in laneItems)
                    {
                        if (other == item || tops[other.Box] <= itemStart + 0.001) continue;
                        if (other.Start >= item.Start + item.Span || other.Start + other.Span <= item.Start) continue;
                        end = Math.Min(end, tops[other.Box] - rowGap);
                    }

                    // A boundary ends the room: an item is not stretched or moved across it.
                    if (fragmenting) end = Math.Min(end, pages!.PageBottomOf(pages.SlotStartingAt(origin + itemStart + HtmlContainerInt.PageBoundaryEpsilon)) - origin);

                    var outer = OuterBlockOf(item.Box);
                    if (end - itemStart <= outer + 0.01) continue;

                    var justify = JustifyOf(item);
                    await PlaceInLanes(item, itemStart, end - itemStart, justify, align);
                }
            }

            // fill-reverse stacks from the end of the block axis: every item moves to the mirror of where it was laid, inside the stacking range;
            // align-content then positions that range in the content box.
            var rangeOffset = onOnePage ? StackingRangeOffset(_gridBox.AlignContent.ToString(), hasDefiniteHeight ? BlockContentSize - stackEnd : 0, fillReverse) : 0;
            foreach (var item in laneItems)
            {
                var top = tops[item.Box];
                var outer = OuterBlockOf(item.Box);
                Shift(item.Box, 0, (fillReverse ? stackEnd - top - outer - top : 0) + rangeOffset);
            }

            if (!_vertical) AlignColumnBaselines([.. inlineBaselines.Values]);

            var area = new Track { Def = GridTrackSize.Auto, Size = stackEnd, Position = origin };
            StateAbsolutelyPositionedAreas(columns, [area], explicitColDefs.Count, 1, colLineNames, rowLineNames);

            SizeLanesBlock(hasDefiniteHeight, stackEnd);

            if (!_vertical) ShrinkInlineLanes(columns);

            if (LanesFragment())
            {
                var contexts = new Dictionary<CssBox, GridSubgridContext>();
                foreach (var item in laneItems)
                    if (LaneSubgridContext(item, columns, null, true, colLineNames, rowLineNames, columnGap, rowGap) is { } context) contexts[item.Box] = context;
                await CommitLanes(g, laneItems, contexts);
            }
        }

        // ─── Rows are the lanes; items stack along the inline axis ──────────────────

        private async ValueTask LayoutRowLanes(Canvas g, List<CssBox> items, bool hasDefiniteHeight, bool inlineIndefinite, double contentWidth,
            double columnGap, double rowGap, GridTemplate? rowTemplate,
            Dictionary<string, List<int>> colLineNames, Dictionary<string, List<int>> rowLineNames)
        {
            var subgrid = _gridBox.SubgridContext;
            var rowSubgrid = rowTemplate?.IsSubgrid == true && subgrid is not null;
            if (rowSubgrid && !_vertical && !_rtl)
            {
                _rowStartInset = _gridBox.ActualMarginTop + _gridBox.ActualBorderTopWidth + _gridBox.ActualPaddingTop;
                _rowEndInset = _gridBox.ActualMarginBottom + _gridBox.ActualBorderBottomWidth + _gridBox.ActualPaddingBottom;
            }

            (int Start, int Length)? rowAutoFitRange = null;
            List<GridTrackSize> explicitRowDefs;
            if (rowSubgrid && subgrid is not null)
                explicitRowDefs = [.. Enumerable.Repeat(GridTrackSize.Auto, subgrid.RowCount)];
            else if (rowTemplate is { AutoRepeat: not GridAutoRepeatKind.None })
                (explicitRowDefs, rowAutoFitRange) = ResolveColumnTemplate(rowTemplate, hasDefiniteHeight ? BlockContentSize : 0, rowGap, _laneRepeatCount);
            else
                explicitRowDefs = ExpandTemplate(rowTemplate);
            var autoRowDefs = ExpandTrackSizes(_gridBox.GridAutoRows);
            var fixedCount = Math.Max(1, explicitRowDefs.Count);

            var laneItems = ResolveLaneItems(items, rowsAreLanes: true, fixedCount, rowLineNames);
            var rowCount = fixedCount;
            foreach (var item in laneItems)
            {
                rowCount = Math.Max(rowCount, item.Span);
                if (item.Start >= 0) rowCount = Math.Max(rowCount, item.Start + item.Span);
            }

            var effectiveRowDefs = hasDefiniteHeight ? explicitRowDefs : [.. explicitRowDefs.Select(PercentToAuto)];
            var effectiveAutoRowDefs = hasDefiniteHeight ? autoRowDefs : [.. autoRowDefs.Select(PercentToAuto)];
            var rowDefs = BuildTrackDefs(effectiveRowDefs, effectiveAutoRowDefs, rowCount);
            var rows = new Track[rowCount];
            for (var i = 0; i < rowCount; i++)
                rows[i] = new Track { Def = rowDefs[i], Size = ResolveFixedRow(rowDefs[i], hasDefiniteHeight) };

            // The stacking axis is the inline axis; with no definite inline size (a vertical container with an auto height) items take their
            // max-content extent and the container is as long as the stack.
            if (inlineIndefinite) contentWidth = 1_000_000;

            var tolerance = ResolveFitTolerance(hasDefiniteHeight ? BlockContentSize : 0);
            var outerHeights = new Dictionary<CssBox, double>();
            var outerWidths = new Dictionary<CssBox, double>();
            var stacks = new Dictionary<CssBox, (int Start, double Top)>();
            var reported = new Dictionary<CssBox, double[]?>();
            var stackEnd = 0.0;

            // What each lane will measure when it is known before the items are: its fixed size, or, in a container of definite height whose
            // other lanes are all fixed, what they leave (which an auto lane stretches to at least).
            var knownLane = new double?[rowCount];
            var provisionalRows = new Track[rowCount];
            {
                var autoLanes = Enumerable.Range(0, rowCount).Where(r => !IsFixed(rows[r].Def)).ToList();
                var fixedTotal = rows.Where(t => IsFixed(t.Def)).Sum(t => t.Size) + rowGap * (rowCount - 1);
                var share = hasDefiniteHeight && autoLanes.Count > 0 && IsStretch(_gridBox.AlignContent.ToString()) && autoLanes.All(r => rows[r].Def.Kind == GridTrackKind.Auto)
                    ? Math.Max(0, BlockContentSize - fixedTotal) / autoLanes.Count
                    : (double?)null;
                for (var r = 0; r < rowCount; r++)
                {
                    knownLane[r] = IsFixed(rows[r].Def) ? rows[r].Size : share;
                    provisionalRows[r] = new Track { Def = rows[r].Def, Size = knownLane[r] ?? 0, Position = r == 0 ? 0 : provisionalRows[r - 1].Position + provisionalRows[r - 1].Size + rowGap };
                }
            }

            var requestedLanes = laneItems.ToDictionary(i => i.Box, i => i.Start);

            // Pass one: every item at its own width (its fit-content size), which fixes where it stacks and, with its height, how tall an
            // auto lane has to be. The lanes are not positioned yet, so the items are measured at the container's origin.
            await StackLanes(laneItems, rowCount, tolerance, columnGap, LanesDense,
                (start, span) => RowSpanHeight(rows, start, span, rowGap),
                async (item, stackStart) =>
                {
                    // A subgrid item reports the sizes of its own rows (report mode) for the lanes to grow to; its stacking axis is its own.
                    // Lanes whose size is known are the item's area height already, which a percentage height or an aspect ratio in it needs.
                    var laneEnd = Math.Min(item.Start + item.Span, rows.Length);
                    var fixedLanes = Enumerable.Range(item.Start, Math.Max(0, laneEnd - item.Start)).All(r => knownLane[r] is not null);
                    var areaHeight = fixedLanes ? provisionalRows.Skip(item.Start).Take(item.Span).Sum(t => t.Size) + rowGap * (item.Span - 1) : 0;
                    item.Box.SubgridContext = LaneSubgridContext(item, null, rows, false, colLineNames, rowLineNames, columnGap, rowGap);
                    if (fixedLanes && item.Box.SubgridContext is { RowCount: > 0 } measuring)
                    {
                        measuring.RowSizes = SpannedSizes(provisionalRows, item.Start, item.Span);
                        measuring.RowOffsets = SpannedOffsets(provisionalRows, item.Start, item.Span);
                    }
                    try
                    {
                        await PlaceItemInCell(g, item.Box, InlineOrigin + stackStart, BlockOrigin, contentWidth, areaHeight, Keywords.Start, Keywords.Start);
                    }
                    finally
                    {
                        reported[item.Box] = item.Box.SubgridContext?.ReportedRowSizes;
                        item.Box.SubgridContext = null;
                    }

                    var outerWidth = OuterInlineOf(item.Box);
                    outerWidths[item.Box] = outerWidth;
                    outerHeights[item.Box] = OuterBlockOf(item.Box);
                    stacks[item.Box] = (item.Start, stackStart);
                    return outerWidth;
                });

            foreach (var item in laneItems)
                stackEnd = Math.Max(stackEnd, stacks[item.Box].Top + outerWidths[item.Box]);

            // An auto-fit lane that no item ended up in collapses, and with it the gap beside it (css-grid-2 7.2.3.2).
            if (!rowSubgrid && rowAutoFitRange is { } rowFit)
            {
                var collapsedRows = new bool[rowCount];
                if (CollapseUnused(collapsedRows, rowFit, laneItems))
                    for (var r = 0; r < rowCount; r++)
                        if (collapsedRows[r])
                        {
                            rows[r].Collapsed = true;
                            rows[r].Size = 0;
                        }
            }

            // With no definite inline size the container is as long as the stack, which the frame of pass two (where `rtl` starts from the bottom)
            // has to know.
            if (inlineIndefinite)
            {
                _gridBox.ActualBottom = _gridBox.ClientTop + stackEnd + _gridBox.ActualPaddingBottom + _gridBox.ActualBorderBottomWidth;
                _frame = WritingModeFrame.For(_gridBox);
            }

            // Every auto-placed item gives its height to every lane it might have been placed in, whether or not it ended up there (css-grid-3 3.4):
            // lanes of an intrinsic size come out alike, and one that holds nothing is as tall as the others.
            foreach (var item in laneItems.Where(i => i.Span == 1 && !IsSubgridItem(i.Box)))
            {
                var anywhere = requestedLanes[item.Box] < 0;
                for (var r = anywhere ? 0 : item.Start; r < (anywhere ? rowCount : item.Start + 1); r++)
                    if (!IsFixed(rows[r].Def) && !rows[r].Collapsed) rows[r].Size = Math.Max(rows[r].Size, outerHeights[item.Box]);
            }

            // A subgrid item's own rows grow the lanes it spans, lane for lane.
            foreach (var item in laneItems.Where(i => reported.GetValueOrDefault(i.Box) is not null))
            {
                var sizes = reported[item.Box]!;
                for (var r = 0; r < sizes.Length && item.Start + r < rowCount; r++)
                    if (!IsFixed(rows[item.Start + r].Def)) rows[item.Start + r].Size = Math.Max(rows[item.Start + r].Size, sizes[r]);
            }

            // A lane's baseline-aligned items share a first baseline (css-align-3 9.3), so the lane is as tall as the largest ascent plus the
            // largest descent among them.
            var baselineExtents = new Dictionary<(int Lane, bool Last), (double Above, double Below)>();
            foreach (var item in laneItems.Where(i => !_vertical && i.Span == 1))
            {
                if (BaselineKind(ResolveSelfAlignment(item.Box.AlignSelf.ToString(), _gridBox.AlignItems.ToString())) is not { } last) continue;
                var span = ItemRowBaseline(item.Box, last);
                var (above, below) = baselineExtents.GetValueOrDefault((item.Start, last));
                baselineExtents[(item.Start, last)] = (Math.Max(above, span.Above), Math.Max(below, outerHeights[item.Box] - span.Above));
            }
            foreach (var ((lane, _), (above, below)) in baselineExtents)
                if (!IsFixed(rows[lane].Def)) rows[lane].Size = Math.Max(rows[lane].Size, above + below);

            foreach (var item in laneItems.Where(i => i.Span > 1 && !IsSubgridItem(i.Box)))
            {
                if (requestedLanes[item.Box] >= 0)
                {
                    GrowSpannedAutoRows(rows, new Placement { Box = item.Box, RowStart = item.Start, RowSpan = item.Span }, outerHeights[item.Box], rowGap);
                    continue;
                }

                // An auto-placed spanning item is assumed to start at every lane: at each it asks the intrinsic lanes it covers for what the
                // fixed ones do not already give it.
                for (var start = 0; start + item.Span <= rowCount; start++)
                {
                    var covered = Enumerable.Range(start, item.Span).ToList();
                    var intrinsic = covered.Where(r => !IsFixed(rows[r].Def) && !rows[r].Collapsed).ToList();
                    if (intrinsic.Count == 0) continue;

                    var taken = covered.Except(intrinsic).Sum(r => rows[r].Size);
                    var share = Math.Max(0, outerHeights[item.Box] - rowGap * (item.Span - 1) - taken) / intrinsic.Count;
                    foreach (var r in intrinsic) rows[r].Size = Math.Max(rows[r].Size, share);
                }
            }

            if (hasDefiniteHeight && rows.Any(t => IsFlexTrack(t.Def)))
                ExpandFlexibleRows(rows, BlockContentSize, rowGap);

            var sizingRowGap = rowGap;
            if (_lanesRowGapResolvesLate)
                rowGap = ParseGap(_gridBox.FlexRowGap, rows.Sum(t => t.Size) + Math.Max(0, rows.Count(t => !t.Collapsed) - 1) * sizingRowGap);

            var rowContainerSize = hasDefiniteHeight ? BlockContentSize
                : rows.Sum(t => t.Size) + Math.Max(0, rows.Count(t => !t.Collapsed) - 1) * sizingRowGap;
            if (hasDefiniteHeight && IsStretch(_gridBox.AlignContent.ToString()))
                StretchAutoRows(rows, rowContainerSize, rowGap);
            PositionTracks(rows, BlockOrigin, rowContainerSize, rowGap, _gridBox.AlignContent.ToString());

            // A row subgrid adopts the parent's lanes, or, in the parent's measuring pass, reports its own sizes back (with its edges added).
            if (rowSubgrid && subgrid is { RowSizes: { } adoptedRowSizes, RowOffsets: { } adoptedRowOffsets })
            {
                rows = BuildAdoptedTracks(adoptedRowSizes, adoptedRowOffsets, BlockOrigin - _rowStartInset, rowCount);
            }
            else if (rowSubgrid && subgrid is not null)
            {
                var reportedRows = rows.Take(subgrid.RowCount).Select(t => t.Size).ToArray();
                if (reportedRows.Length > 0)
                {
                    reportedRows[0] += _rowStartInset;
                    reportedRows[^1] += _rowEndInset;
                }

                subgrid.ReportedRowSizes = reportedRows;
            }

            var trackReverse = LanesTrackReverse;
            if (trackReverse)
                foreach (var row in rows)
                    row.Position = 2 * BlockOrigin + rowContainerSize - row.Position - row.Size;

            // Pass two: each item into its lanes, aligned across them by align-self (stretch fills a lane, as in a grid). Along the inline axis it
            // is aligned in the room up to what follows it in its lane (or the end of the stacking range), as a column lane's item is in its height.
            // fill-reverse stacks from the end of the inline axis instead of the start.
            var fillReverse = LanesFillReverse;
            var inlineExtent = inlineIndefinite ? stackEnd : contentWidth;
            var extent = PlacesRangeExplicitly(_gridBox.JustifyContent.ToString()) ? stackEnd : inlineExtent;
            var rangeOffset = StackingRangeOffset(_gridBox.JustifyContent.ToString(), inlineExtent - stackEnd, fillReverse);
            foreach (var item in laneItems)
            {
                var (start, top) = stacks[item.Box];
                var span = item.Span;
                var cellY = rows[trackReverse ? start + span - 1 : start].Position + (start == 0 ? _rowStartInset : 0);
                var cellHeight = RowSpanHeight(rows, start, span, rowGap)
                    - (start == 0 ? _rowStartInset : 0) - (start + span >= rows.Length ? _rowEndInset : 0);
                var align = ResolveSelfAlignment(item.Box.AlignSelf.ToString(), _gridBox.AlignItems.ToString());
                if (!_vertical && span == 1 && BaselineKind(align) is true) align = Keywords.End;
                var outerWidth = outerWidths[item.Box];

                var room = outerWidth;
                var justify = Keywords.Start;
                if (!fillReverse)
                {
                    var end = extent;
                    foreach (var other in laneItems)
                    {
                        var (otherStart, otherTop) = stacks[other.Box];
                        if (other == item || otherTop <= top + 0.001) continue;
                        if (otherStart >= start + span || otherStart + other.Span <= start) continue;
                        end = Math.Min(end, otherTop - columnGap);
                    }

                    // As in a column lane's height, `normal` is `start` in the stacking axis: only an explicit alignment uses the room.
                    var itemJustify = LogicalizePhysicalKeyword(ResolveSelfAlignment(item.Box.JustifySelf.ToString(), _gridBox.JustifyItems.ToString()));
                    if (end - top > outerWidth + 0.01 && !itemJustify.Isi(Keywords.Normal))
                    {
                        room = end - top;
                        justify = itemJustify;
                    }
                }

                var x = (fillReverse ? InlineOrigin + stackEnd - top - outerWidth : InlineOrigin + top) + rangeOffset;
                item.Box.SubgridContext = LaneSubgridContext(item, null, rows, true, colLineNames, rowLineNames, columnGap, rowGap);
                try
                {
                    await PlaceItemInCell(g, item.Box, x, cellY, room, cellHeight, justify, align);
                }
                finally
                {
                    item.Box.SubgridContext = null;
                }
            }

            if (!_vertical) AlignRowBaselines([.. laneItems.Select(i => new Placement { Box = i.Box, RowStart = i.Start, RowSpan = i.Span })]);

            var column = new Track { Def = GridTrackSize.Auto, Size = Math.Max(inlineExtent, stackEnd), Position = InlineOrigin };
            StateAbsolutelyPositionedAreas([column], rows, 1, explicitRowDefs.Count, colLineNames, rowLineNames);

            SizeLanesBlock(hasDefiniteHeight, rowContainerSize);

            if (!_vertical && _gridBox.DerivedStyle.ActualDisplay == Keywords.InlineGrid && !CssValueParser.IsValidLength(_gridBox.Width))
                _gridBox.ActualRight = InlineOrigin + stackEnd + _gridBox.ActualPaddingRight + _gridBox.ActualBorderRightWidth;

            // The rows are the lanes, so the grid's own relocation and commit machinery apply: a row moves whole across a boundary and its items
            // commit together.
            if (LanesFragment())
            {
                var placements = laneItems.Select(i => new Placement { Box = i.Box, RowStart = i.Start, RowSpan = i.Span }).ToList();
                RelocateRowsAcrossFragmentainers(placements);

                var contexts = new Dictionary<CssBox, GridSubgridContext>();
                foreach (var item in laneItems)
                    if (LaneSubgridContext(item, null, rows, true, colLineNames, rowLineNames, columnGap, rowGap) is { } context) contexts[item.Box] = context;
                await CommitItemContent(g, BuildRowGroups(placements), 0, null, null, contexts, _gridBox.Location);
            }
        }

        /// <summary>An <c>inline-grid-lanes</c> container with an auto width is as wide as its lanes, like an inline-grid.</summary>
        private void ShrinkInlineLanes(Track[] columns)
        {
            if (_gridBox.DerivedStyle.ActualDisplay != Keywords.InlineGrid || CssValueParser.IsValidLength(_gridBox.Width) || columns.Length == 0)
                return;
            var contentRight = columns.Max(c => c.Position + c.Size);
            _gridBox.ActualRight = contentRight + _gridBox.ActualPaddingRight + _gridBox.ActualBorderRightWidth;
        }
    }
}
