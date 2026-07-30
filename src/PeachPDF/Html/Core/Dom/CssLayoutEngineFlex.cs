using PeachPDF;
using PeachPDF.Html.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core.Entities;
using PeachPDF.Html.Core.Fragmentation;
using PeachPDF.Html.Core.Parse;
using PeachPDF.Html.Core.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PeachPDF.Html.Core.Dom
{
    internal sealed class CssLayoutEngineFlex
    {
        private readonly CssBox _flexBox;
        private bool _isRow;
        private bool _isReverse;
        private bool _isWrap;
        private bool _isWrapReverse;

        private CssLayoutEngineFlex(CssBox flexBox)
        {
            _flexBox = flexBox;
        }

        /// <param name="g">the graphics context layout is running against</param>
        /// <param name="flexBox">the flex container to lay out</param>
        /// <param name="resume">
        /// a <see cref="FlexBreakToken"/> naming which of this container's items did not finish their own
        /// content on an earlier fragmentainer pass, or null to lay the container out from the start. Only
        /// a single-line, row-direction container currently publishes one — see the commit-pass remarks on
        /// <see cref="RelocateLinesAcrossFragmentainers"/>'s sibling below.
        /// </param>
        public static async ValueTask PerformLayout(RGraphics g, CssBox flexBox, BreakToken? resume = null)
        {
            try
            {
                await new CssLayoutEngineFlex(flexBox).Layout(g, resume);
            }
            catch (Exception ex)
            {
                if (flexBox.HtmlContainer is { } container)
                    throw container.RenderError(HtmlRenderErrorType.Layout, "Failed flex layout", ex);
            }
        }

        private async ValueTask Layout(RGraphics g, BreakToken? resume)
        {
            // A resumed pass re-enters only the items that did not finish their own content last time -
            // every earlier phase (measurement, sizing, line/main/cross positioning) already ran and its
            // results are still sitting on the live box tree, untouched, because nothing here re-derives
            // them. Re-running any of that would re-measure a container whose free space has already been
            // distributed and whose items have already been translated into place.
            if (resume is FlexBreakToken flexResume)
            {
                await ResumeCommitPass(g, flexResume);
                return;
            }

            ParseFlexDirection();
            ParseFlexWrap();

            // Compute container main-axis size
            var containerWidth = await CssLayoutEngine.GetBoxWidth(g, _flexBox);
            _flexBox.ActualRight = _flexBox.Location.X + containerWidth + _flexBox.ActualBoxSizeIncludedWidth;
            _flexBox.ActualBottom = _flexBox.Location.Y;

            // Pre-apply a definite height when one exists, so ClientBottom is correct for cross-axis sizing.
            // A definite height comes either from an explicit `height` length or — when the height is auto —
            // from a preferred `aspect-ratio` against the (now-known) definite width (CSS Box Sizing 4 §5).
            // Both make the container's cross size (row) / main size (column) definite, which stretch and
            // percentage-height children resolve against — the Charts.css `tbody { aspect-ratio: … }` case.
            bool hasExplicitHeight = CssValueParser.IsValidLength(_flexBox.Height);
            bool hasDefiniteHeight = hasExplicitHeight
                || CssLayoutEngine.TryGetAspectRatioHeight(_flexBox, out _);
            if (hasDefiniteHeight)
            {
                var fullHeight = CssLayoutEngine.GetBoxHeight(_flexBox) ?? 0;
                _flexBox.ActualBottom = _flexBox.Location.Y + fullHeight;
            }

            double mainSize = _isRow
                ? _flexBox.ClientRight - _flexBox.ClientLeft
                : (_flexBox.ClientBottom - _flexBox.ClientTop);

            // Container cross-axis size (0 when auto/unknown until content lays out)
            double containerCrossSize = _isRow
                ? (hasDefiniteHeight ? _flexBox.ClientBottom - _flexBox.ClientTop : 0)
                : containerWidth;

            // Whether the main axis has indefinite size (column + auto height = no grow/shrink)
            bool mainSizeIndefinite = !_isRow && !hasDefiniteHeight;

            // Phase 1: collect and order flex items.
            // Anonymous whitespace-only boxes between flex items must be discarded per CSS spec.
            var rawItems = _flexBox.Boxes
                .Where(b => b.Display != CssConstants.None && !b.IsOutOfFlow
                            && (b.HtmlTag != null || !b.IsSpaceOrEmpty))
                .OrderBy(ParseOrder)
                .ThenBy(b => _flexBox.Boxes.IndexOf(b))
                .ToList();

            if (rawItems.Count == 0)
            {
                if (!hasDefiniteHeight)
                    _flexBox.ActualBottom = _flexBox.Location.Y + _flexBox.ActualBoxSizeIncludedHeight;
                return;
            }

            // Phase 2: measure each item; derive hypothetical main size from CSS (not from layout result)
            var items = new List<FlexItem>(rawItems.Count);
            foreach (var box in rawItems)
            {
                var item = await MeasureItem(g, box, mainSize, mainSizeIndefinite);

                // Resolve main-axis margins up front: 0 for "auto" until Phase 7 distributes
                // free space into it, otherwise the item's actual parsed margin.
                item.MarginBeforeAuto = IsMainMarginBeforeAuto(box);
                item.MarginAfterAuto  = IsMainMarginAfterAuto(box);
                item.MarginBefore = item.MarginBeforeAuto ? 0 : MainMarginBefore(box);
                item.MarginAfter  = item.MarginAfterAuto  ? 0 : MainMarginAfter(box);

                items.Add(item);
            }

            // For column with indefinite main size, mainSize = sum of hypothetical sizes (no grow/shrink) + gaps
            if (mainSizeIndefinite)
            {
                double mainGapCol = ParseMainGap(0);
                int nc = items.Count;
                mainSize = items.Sum(i => i.HypotheticalMainSize + i.MarginBefore + i.MarginAfter)
                    + (nc > 1 ? mainGapCol * (nc - 1) : 0);
            }

            // Phase 3: collect into flex lines
            var lines = CollectLines(items, mainSize);

            // Phase 4: resolve flex-grow / flex-shrink
            if (!mainSizeIndefinite)
            {
                foreach (var line in lines)
                    await ResolveFlexibleLengths(g, line, mainSize);
            }
            else
            {
                foreach (var line in lines)
                    foreach (var item in line.Items)
                    {
                        double final = ClampMainAxis(item.Box, item.HypotheticalMainSize, mainSize);
                        item.FinalMainSize = final;
                        if (Math.Abs(final - item.NaturalMainSize) > 0.5)
                            await ResizeItem(g, item, final);
                    }
            }

            // Phase 4b: shrink non-stretch column items to their fit-content cross (width) size.
            // A column item's cross axis is width, and a blockified auto-width block fills the whole
            // container during layout, so without this every item reports ActualBoxSizingWidth == container
            // width — making align-items/align-self center/flex-end/flex-start have nothing to offset against
            // and stretch a no-op (issue #133). Row items get correct fit-content cross sizing for free
            // because their cross axis is height, which block layout already shrink-wraps. Only non-stretch,
            // auto-width items are shrunk, so the default (stretch) behavior is unchanged.
            if (!_isRow)
            {
                foreach (var line in lines)
                    foreach (var item in line.Items)
                        await ShrinkColumnItemToContentWidth(g, item, containerCrossSize, mainSize);
            }

            // Phase 5: line cross sizes; for single-line, respect container cross size if set
            foreach (var line in lines)
            {
                double natural = ComputeLineCrossSize(line);
                line.CrossSize = (!_isWrap && containerCrossSize > 0)
                    ? Math.Max(natural, containerCrossSize)
                    : natural;
            }

            // Phase 6: align-content (multi-line cross-axis distribution)
            double crossGap = ParseCrossGap(mainSize);
            double totalCrossGap = lines.Count > 1 ? crossGap * (lines.Count - 1) : 0;
            double totalCross = lines.Sum(l => l.CrossSize);
            double crossFree = Math.Max(0, containerCrossSize - totalCross - totalCrossGap);
            // A column container's cross axis is the inline one, which is resolved before any of this.
            DistributeCrossSpace(lines, crossFree, crossGap, containerCrossSize, !_isRow || hasDefiniteHeight);

            // Phase 7: justify-content — main-axis positions
            foreach (var line in lines)
                ComputeMainOffsets(line, mainSize, mainSizeIndefinite);

            // Phase 8: align-items / align-self — cross-axis positions
            foreach (var line in lines)
                await ComputeCrossOffsets(g, line);

            // For column with auto (indefinite) height: set ActualBottom now so that
            // containerMainEnd is correct for column-reverse positioning in AssignLocations.
            if (!_isRow && mainSizeIndefinite)
            {
                _flexBox.ActualBottom = _flexBox.ClientTop + mainSize
                    + _flexBox.ActualPaddingBottom + _flexBox.ActualBorderBottomWidth;
            }

            // Phase 9: assign final locations
            AssignLocations(lines);

            // Phase 10: update container size if auto.
            // Use Max across all lines because wrap-reverse can make the last line have the smallest offset.
            double maxCrossEnd = lines.Count > 0
                ? lines.Max(l => l.CrossOffset + l.CrossSize)
                : 0;

            if (!hasDefiniteHeight)
            {
                if (_isRow)
                    _flexBox.ActualBottom = _flexBox.ClientTop + maxCrossEnd
                        + _flexBox.ActualPaddingBottom + _flexBox.ActualBorderBottomWidth;
                // A column container's cross axis is the inline one, and `hasDefiniteHeight` says nothing
                // about it: a container with a `width` has been sized already, and sizing it again from
                // where its lines happen to end would *discard* that width wherever the lines do not
                // reach the far edge — which is anywhere align-content has free space to distribute.
                else if (!CssValueParser.IsValidLength(_flexBox.Width))
                    _flexBox.ActualRight = _flexBox.ClientLeft + maxCrossEnd
                        + _flexBox.ActualPaddingRight + _flexBox.ActualBorderRightWidth;
            }

            // Phase 9b: the break points between lines are real ones now that the items sit where they
            // will finally sit - see RelocateLinesAcrossFragmentainers. Run *after* the container has
            // been sized from its lines, because that sizing reads the line offsets rather than the
            // boxes and so would overwrite the displacement this adds: an auto-height container whose
            // lines were pushed onto the next fragmentainer reported a height a whole displacement short
            // of the content it holds.
            RelocateLinesAcrossFragmentainers(lines);

            // Phase 9c: fragment each item's own content for real, now that every item sits at the
            // position it will finally hold. See CommitItemContent's remarks.
            await CommitItemContent(g, lines);

            // Phase 10b: inline-flex shrinks to content in the main axis (like inline-block).
            // For row direction with auto width, update ActualRight to the actual content extent.
            bool hasExplicitWidth = CssValueParser.IsValidLength(_flexBox.Width);
            if (_flexBox.Display == CssConstants.InlineFlex && _isRow && !hasExplicitWidth && lines.Count > 0)
            {
                double contentMainEnd = lines.Max(l =>
                    l.Items.Count > 0
                        ? l.Items.Last().MainOffset + l.Items.Last().FinalMainSize + l.Items.Last().MarginAfter
                        : 0.0);
                _flexBox.ActualRight = _flexBox.ClientLeft + contentMainEnd
                    + _flexBox.ActualPaddingRight + _flexBox.ActualBorderRightWidth;
            }
        }

        // ─── Phase 2: measurement ────────────────────────────────────────────────

        private async ValueTask<FlexItem> MeasureItem(RGraphics g, CssBox box, double mainSize, bool mainSizeIndefinite)
        {
            // Derive hypothetical main size from CSS properties (don't rely on PerformLayout result,
            // since auto-width block boxes fill the entire containing block instead of their intrinsic size).
            // A percentage flex-basis against an indefinite main axis resolves to nothing per spec
            // (§4.5), so it must fall through to auto/content-based sizing rather than resolving to 0.
            bool isIndefinitePercentageBasis = mainSizeIndefinite && box.FlexBasis.EndsWith('%');
            double hypothetical;
            if (box.FlexBasis is not ("auto" or "content" or "") && !isIndefinitePercentageBasis)
            {
                // CSS flex-basis = content size; hypothetical = outer size = content + padding + border
                hypothetical = CssValueParser.ParseLength(box.FlexBasis, mainSize, box) + MainPaddingBorder(box);
            }
            else if (box.FlexBasis != "content" && _isRow && CssValueParser.IsValidLength(box.Width))
            {
                hypothetical = CssValueParser.ParseLength(box.Width, mainSize, box) + MainPaddingBorder(box);
            }
            else if (box.FlexBasis != "content" && !_isRow && CssValueParser.IsValidLength(box.Height))
            {
                hypothetical = CssValueParser.ParseLength(box.Height, mainSize, box) + MainPaddingBorder(box);
            }
            else
            {
                // Auto width/basis: run PerformLayout to get cross-axis size and word positions.
                box.Location = new RPoint(_flexBox.ClientLeft, _flexBox.ClientTop);
                box.ActualBottom = box.Location.Y;
                // A first-ever layout of box has nothing to reset, but this item can be measured more
                // than once within one document-layout generation - most directly, a nested flex/grid
                // container re-enters its own Phase 1 (and so this method) every time an ancestor's own
                // commit pass (CommitItemContent) re-lays its content out. box's own prologue only runs
                // once per generation, so nothing else clears its per-line rectangles between those
                // calls; without this, FlowBox measures new word positions on top of the previous call's
                // un-cleared line boxes, corrupting the wrap this item's own hypothetical size depends on.
                box.RectanglesReset();
                await PerformLayoutBlockified(g, box);

                // naturalMain = layout result; for row direction this is the block-fill width (container width).
                double naturalMain = _isRow ? box.ActualBoxSizingWidth : box.ActualBoxSizingHeight;

                if (ParseFloat(box.FlexGrow) > 0)
                {
                    // flex-grow items: hypothetical=0 so all free space is distributed via growth.
                    hypothetical = 0;
                }
                else if (_isRow)
                {
                    // Row, no flex-grow: derive max-content width from inline word measurements.
                    // Block items fill the container on layout, so naturalMain = container width.
                    // For inline-only boxes the actual content width is the sum of word widths per line.
                    double maxContent;
                    if (DomUtils.ContainsInlinesOnly(box) && box.LineBoxes.Count > 0)
                    {
                        double lineWidth = box.LineBoxes.Max(lb => lb.Words.Sum(w => w.FullWidth));
                        // Add a sub-pixel epsilon so that when this width is used as the explicit
                        // content size in ResizeItem, the same words don't spuriously wrap due to
                        // IEEE 754 rounding differences between (a+b)+c and (a+c)+b.
                        maxContent = lineWidth + 0.01
                            + box.ActualPaddingLeft + box.ActualPaddingRight
                            + box.ActualBorderLeftWidth + box.ActualBorderRightWidth;
                    }
                    else
                    {
                        // Block children: no word measurement; use container fill width as fallback.
                        maxContent = naturalMain;
                    }
                    // min-width constrains content width; outer minimum = min-width + padding + border
                    if (box.MinWidth != "0" && CssValueParser.IsValidLength(box.MinWidth))
                    {
                        double minOuter = CssValueParser.ParseLength(box.MinWidth, mainSize, box)
                            + box.ActualPaddingLeft + box.ActualPaddingRight
                            + box.ActualBorderLeftWidth + box.ActualBorderRightWidth;
                        maxContent = Math.Max(maxContent, minOuter);
                    }
                    hypothetical = maxContent;
                }
                else
                {
                    // Column direction: ActualBoxSizingHeight is the natural content height (not container-fill).
                    hypothetical = naturalMain;
                }

                return new FlexItem(box, naturalMain, hypothetical);
            }

            // Layout at hypothetical size so we get an accurate cross-axis dimension.
            // hypothetical = outer size; CSS width/height property = content size = outer - padding - border.
            string? savedDim = null;
            if (hypothetical > 0)
            {
                double cssContentSize = Math.Max(0, hypothetical - MainPaddingBorder(box));
                if (_isRow) { savedDim = box.Width;  box.Width  = FormatLayoutUnits(cssContentSize); }
                else        { savedDim = box.Height; box.Height = FormatLayoutUnits(cssContentSize); }
            }

            box.Location = new RPoint(_flexBox.ClientLeft, _flexBox.ClientTop);
            box.ActualBottom = box.Location.Y;
            // See the identical reset above: this item can be measured more than once per generation.
            box.RectanglesReset();
            await PerformLayoutBlockified(g, box);

            if (savedDim != null)
            {
                if (_isRow) box.Width  = savedDim;
                else        box.Height = savedDim;
            }

            // NaturalMainSize = what PerformLayout actually produced (used to detect resize need)
            double naturalMain2 = _isRow ? box.ActualBoxSizingWidth : box.ActualBoxSizingHeight;

            return new FlexItem(box, naturalMain2, hypothetical);
        }

        // ─── Phase 3: line collection ─────────────────────────────────────────────

        private List<FlexLine> CollectLines(List<FlexItem> items, double mainSize)
        {
            if (!_isWrap)
                return [new FlexLine(items)];

            double mainGap = ParseMainGap(mainSize);
            var lines = new List<FlexLine>();
            var current = new List<FlexItem>();
            double used = 0;

            foreach (var item in items)
            {
                double itemMain = item.HypotheticalMainSize
                    + item.MarginBefore + item.MarginAfter;
                if (current.Count > 0 && used + mainGap + itemMain > mainSize)
                {
                    lines.Add(new FlexLine(current));
                    current = [];
                    used = 0;
                }
                if (current.Count > 0) used += mainGap;
                current.Add(item);
                used += itemMain;
            }

            if (current.Count > 0)
                lines.Add(new FlexLine(current));

            return lines;
        }

        // ─── Phase 4: flexible length resolution ──────────────────────────────────

        private async ValueTask ResolveFlexibleLengths(RGraphics g, FlexLine line, double mainSize)
        {
            double mainGap = ParseMainGap(mainSize);
            double totalGapSpace = line.Items.Count > 1 ? mainGap * (line.Items.Count - 1) : 0;
            double usedSpace = line.Items.Sum(i =>
                i.HypotheticalMainSize + i.MarginBefore + i.MarginAfter);
            double freeSpace = mainSize - usedSpace - totalGapSpace;

            foreach (var item in line.Items)
            {
                double final;
                if (freeSpace > 0)
                {
                    double totalGrow = line.Items.Sum(i => ParseFloat(i.Box.FlexGrow));
                    var grow = ParseFloat(item.Box.FlexGrow);
                    final = totalGrow > 0
                        ? item.HypotheticalMainSize + freeSpace * (grow / totalGrow)
                        : item.HypotheticalMainSize;
                }
                else if (freeSpace < 0)
                {
                    double totalShrink = line.Items.Sum(i =>
                        ParseFloat(i.Box.FlexShrink) * i.HypotheticalMainSize);
                    var shrinkFactor = ParseFloat(item.Box.FlexShrink) * item.HypotheticalMainSize;
                    final = totalShrink > 0
                        ? Math.Max(0, item.HypotheticalMainSize + freeSpace * (shrinkFactor / totalShrink))
                        : item.HypotheticalMainSize;
                }
                else
                {
                    final = item.HypotheticalMainSize;
                }

                final = ClampMainAxis(item.Box, final, mainSize);
                item.FinalMainSize = final;

                // Re-layout only when the final size differs from what was used during measurement
                if (Math.Abs(final - item.NaturalMainSize) > 0.5)
                    await ResizeItem(g, item, final);
            }
        }

        private async ValueTask ResizeItem(RGraphics g, FlexItem item, double finalSize)
        {
            // finalSize is the outer size (content + padding + border); CSS property takes content size only.
            string saved;
            double cssContentSize = Math.Max(0, finalSize - MainPaddingBorder(item.Box));
            if (_isRow)
            {
                saved = item.Box.Width;
                item.Box.Width = FormatLayoutUnits(cssContentSize);
            }
            else
            {
                saved = item.Box.Height;
                item.Box.Height = FormatLayoutUnits(cssContentSize);
            }

            item.Box.Location = new RPoint(_flexBox.ClientLeft, _flexBox.ClientTop);
            item.Box.ActualBottom = item.Box.Location.Y;
            item.Box.RectanglesReset();
            await PerformLayoutBlockified(g, item.Box);

            if (_isRow) item.Box.Width  = saved;
            else        item.Box.Height = saved;
        }

        // ─── Phase 4b: column cross-axis (width) shrink-to-fit ────────────────────

        /// <summary>
        /// For a column-direction flex item whose resolved alignment is not <c>stretch</c> and whose width is
        /// auto, re-lays the item out at its fit-content (max-content, capped at the container cross size)
        /// width, so <c>ActualBoxSizingWidth</c> reflects the content width instead of the
        /// container-fill width a blockified auto-width block produces. This is what lets the existing
        /// cross-axis positioning (<see cref="ComputeCrossOffsets"/>/<see cref="AssignLocations"/>) actually
        /// center / end-align / start-align the item (CSS Flexbox 1 §7.5 hypothetical cross size, §8.3). A
        /// stretch item (or the default, since <c>align-items</c> defaults to <c>normal</c> ≡ stretch) is left
        /// full-width, and a definite-width item is left at its width.
        /// </summary>
        private async ValueTask ShrinkColumnItemToContentWidth(RGraphics g, FlexItem item, double containerCrossSize, double mainSize)
        {
            var box = item.Box;

            // Resolve the item's effective cross-axis alignment (same idiom as ComputeCrossOffsets).
            var align = box.AlignSelf is "auto" or "" ? _flexBox.AlignItems : box.AlignSelf;
            if (align is CssConstants.Stretch or "normal") return;

            // Only auto-width items shrink; a definite width is already the item's cross size.
            if (CssValueParser.IsValidLength(box.Width)) return;

            // Available cross size: the container's inner width minus the item's own cross margins (0 when
            // the container cross size is unknown → no cap). Subtracting the margins keeps an overflowing item
            // (content wider than the container) at the container-minus-margins width block layout already
            // gave it, rather than re-expanding it to the full container width and pushing the margins into
            // overflow.
            double crossMargins = box.ActualMarginLeft + box.ActualMarginRight;
            double available = containerCrossSize > 0 ? Math.Max(0, containerCrossSize - crossMargins) : double.MaxValue;
            double fitOuter = await CssLayoutEngine.GetFitContentWidth(g, box, available);

            // Clamp by the cross-axis min/max-width (outer sizes), min winning over max per CSS 2.1 §10.4.
            double crossPaddingBorder = box.ActualPaddingLeft + box.ActualPaddingRight
                                        + box.ActualBorderLeftWidth + box.ActualBorderRightWidth;
            if (CssValueParser.IsValidLength(box.MaxWidth))
                fitOuter = Math.Min(fitOuter, CssValueParser.ParseLength(box.MaxWidth, containerCrossSize, box) + crossPaddingBorder);
            if (box.MinWidth != "0" && CssValueParser.IsValidLength(box.MinWidth))
                fitOuter = Math.Max(fitOuter, CssValueParser.ParseLength(box.MinWidth, containerCrossSize, box) + crossPaddingBorder);

            // Nothing to do if it already matches (e.g. content wider than the container → full width).
            if (Math.Abs(fitOuter - box.ActualBoxSizingWidth) <= 0.5) return;

            // Re-lay out at the fit-content width, locking the main-axis height to its resolved size —
            // mirroring the column stretch re-layout branch in ComputeCrossOffsets.
            var savedWidth = box.Width;
            var savedHeight = box.Height;
            box.Width = FormatLayoutUnits(Math.Max(0, fitOuter - crossPaddingBorder));
            box.Height = FormatLayoutUnits(Math.Max(0, item.FinalMainSize - MainPaddingBorder(box)));
            box.Location = new RPoint(_flexBox.ClientLeft, _flexBox.ClientTop);
            box.ActualBottom = box.Location.Y;
            box.RectanglesReset();
            await PerformLayoutBlockified(g, box);
            box.Width = savedWidth;
            box.Height = savedHeight;
        }

        // ─── Phase 5: cross sizes ─────────────────────────────────────────────────

        private double ComputeLineCrossSize(FlexLine line)
        {
            if (line.Items.Count == 0) return 0;
            return line.Items.Max(i =>
                _isRow
                    ? i.Box.ActualBoxSizingHeight + i.Box.ActualMarginTop  + i.Box.ActualMarginBottom
                    : i.Box.ActualBoxSizingWidth  + i.Box.ActualMarginLeft + i.Box.ActualMarginRight);
        }

        // ─── Phase 6: align-content ───────────────────────────────────────────────

        /// <summary>
        /// Places every line on the cross axis: <c>align-content</c> against the container's free cross
        /// space, then <c>flex-wrap: wrap-reverse</c>'s reversal of the stacking direction.
        /// </summary>
        /// <param name="lines">the container's lines, in flow order</param>
        /// <param name="remaining">free cross space left over once every line has its cross size</param>
        /// <param name="crossGap">the used cross-axis gap between two adjacent lines</param>
        /// <param name="containerCrossSize">the container's inner cross size</param>
        /// <param name="crossSizeIsDefinite">
        /// whether <paramref name="containerCrossSize"/> is the container's own definite size, rather
        /// than a placeholder for one the lines themselves decide. Always true in a column direction,
        /// where the cross axis is the (already resolved) inline one
        /// </param>
        private void DistributeCrossSpace(List<FlexLine> lines, double remaining, double crossGap,
            double containerCrossSize, bool crossSizeIsDefinite)
        {
            if (lines.Count == 0) return;

            double offset = 0;
            switch (_flexBox.AlignContent)
            {
                case CssConstants.FlexEnd:
                case "end":
                    offset = remaining;
                    foreach (var l in lines) { l.CrossOffset = offset; offset += l.CrossSize + crossGap; }
                    break;
                case CssConstants.Center:
                    offset = remaining / 2;
                    foreach (var l in lines) { l.CrossOffset = offset; offset += l.CrossSize + crossGap; }
                    break;
                case CssConstants.SpaceBetween:
                {
                    double spacing = lines.Count > 1 ? remaining / (lines.Count - 1) : 0;
                    foreach (var l in lines) { l.CrossOffset = offset; offset += l.CrossSize + crossGap + spacing; }
                    break;
                }
                case CssConstants.SpaceAround:
                {
                    double spacing = remaining / lines.Count;
                    offset = spacing / 2;
                    foreach (var l in lines) { l.CrossOffset = offset; offset += l.CrossSize + crossGap + spacing; }
                    break;
                }
                case CssConstants.SpaceEvenly:
                {
                    double spacing = remaining / (lines.Count + 1);
                    offset = spacing;
                    foreach (var l in lines) { l.CrossOffset = offset; offset += l.CrossSize + crossGap + spacing; }
                    break;
                }
                case CssConstants.Stretch:
                {
                    double extra = lines.Count > 0 ? remaining / lines.Count : 0;
                    foreach (var l in lines)
                    {
                        l.CrossSize += extra;
                        l.CrossOffset = offset;
                        offset += l.CrossSize + crossGap;
                    }
                    break;
                }
                default: // flex-start / normal
                    foreach (var l in lines) { l.CrossOffset = offset; offset += l.CrossSize + crossGap; }
                    break;
            }

            if (!_isWrapReverse) return;

            // `wrap-reverse` swaps the cross-start and cross-end directions
            // (https://www.w3.org/TR/css-flexbox-1/#flex-wrap-property), so the lines are stacked the
            // other way round — each still occupying its own cross size, in sequence, with whatever
            // align-content put between them. Reflecting each line's placed strip about the middle of
            // the container's cross axis is exactly that stack: it reverses the order while every line
            // keeps its own size and every gap keeps its own width, read in the new direction.
            //
            // Permuting the *offsets* instead — giving line[i] the offset computed for line[n-1-i] —
            // is only the same thing when every line has the same cross size. Where they differ, a line
            // lands at an offset computed for a line of another size and the two overlap, and the
            // container is sized from the wrong end of the stack (issue #458).
            //
            // The reflection is about the container's own cross size, so lines that do not fit overflow
            // the cross-end edge, which wrap-reverse has put at the top (row) / left (column). Only an
            // *indefinite* cross size reflects about the lines' own extent instead — there it is the
            // lines that decide the container's size, so nothing can overflow it. A definite size of
            // zero is a real size and belongs to the first case, not the second.
            double crossExtent = crossSizeIsDefinite
                ? Math.Max(0, containerCrossSize)
                : lines.Max(l => l.CrossOffset + l.CrossSize);

            foreach (var l in lines)
                l.CrossOffset = crossExtent - (l.CrossOffset + l.CrossSize);
        }

        // ─── Phase 7: justify-content ─────────────────────────────────────────────

        private void ComputeMainOffsets(FlexLine line, double mainSize, bool indefiniteMainSize)
        {
            double mainGap = ParseMainGap(mainSize);
            double totalGapSpace = line.Items.Count > 1 ? mainGap * (line.Items.Count - 1) : 0;

            double usedSpace = line.Items.Sum(i =>
                i.FinalMainSize + i.MarginBefore + i.MarginAfter);
            double freeSpace = indefiniteMainSize ? 0 : mainSize - usedSpace - totalGapSpace;
            int n = line.Items.Count;

            // Auto margins on the main axis absorb free space before justify-content runs
            // (spec §8.1). A negative freeSpace (overflow) leaves auto margins at zero.
            int autoMarginCount = line.Items.Sum(i => (i.MarginBeforeAuto ? 1 : 0) + (i.MarginAfterAuto ? 1 : 0));
            if (autoMarginCount > 0 && freeSpace > 0)
            {
                double share = freeSpace / autoMarginCount;
                foreach (var i in line.Items)
                {
                    if (i.MarginBeforeAuto) i.MarginBefore = share;
                    if (i.MarginAfterAuto)  i.MarginAfter  = share;
                }
                freeSpace = 0;
            }

            double startOffset, spacing;
            switch (_flexBox.JustifyContent)
            {
                case CssConstants.FlexEnd:
                case "end":
                    startOffset = freeSpace; spacing = 0; break;
                case CssConstants.Center:
                    startOffset = freeSpace / 2; spacing = 0; break;
                case CssConstants.SpaceBetween:
                    startOffset = 0; spacing = n > 1 ? freeSpace / (n - 1) : 0; break;
                case CssConstants.SpaceAround:
                    spacing = n > 0 ? freeSpace / n : 0;
                    startOffset = spacing / 2; break;
                case CssConstants.SpaceEvenly:
                    spacing = n > 0 ? freeSpace / (n + 1) : 0;
                    startOffset = spacing; break;
                default: // flex-start / normal
                    startOffset = 0; spacing = 0; break;
            }

            double cursor = startOffset;
            foreach (var item in line.Items)
            {
                item.MainOffset = cursor + item.MarginBefore;
                cursor += item.FinalMainSize + item.MarginBefore + item.MarginAfter + mainGap + spacing;
            }
        }

        // ─── Phase 8: align-items / align-self ───────────────────────────────────

        private async ValueTask ComputeCrossOffsets(RGraphics g, FlexLine line)
        {
            // Pre-compute baseline offsets for items using baseline alignment. Per spec §8.5,
            // baseline alignment only applies when the cross axis is vertical (row-direction
            // flex); column-direction flex has no text-baseline concept on its cross axis and
            // falls through to the flex-start fallback below. An item's offset is omitted when
            // it has no line-box content anywhere (e.g. an empty or image-only item), which
            // also falls back to flex-start.
            double maxBaseline = 0;
            // The same distance measured to the *other* end: from an item's baseline to its cross-end
            // margin edge. §8.3 flushes the baseline-sharing group against the line's cross-start edge,
            // which `wrap-reverse` has moved to the far side, so the item that lands flush there is the
            // one furthest from the baseline in that direction — not the one with the largest ascent.
            double maxBaselineTail = 0;
            Dictionary<FlexItem, double>? baselineOffsets = null;
            if (_isRow)
            {
                foreach (var item in line.Items)
                {
                    var itemAlign = item.Box.AlignSelf is "auto" or "" ? _flexBox.AlignItems : item.Box.AlignSelf;
                    if (itemAlign != CssConstants.Baseline) continue;

                    var offset = BaselineAlignment.GetItemBaselineOffset(item.Box);
                    if (offset is null) continue;

                    baselineOffsets ??= [];
                    baselineOffsets[item] = offset.Value;
                    maxBaseline = Math.Max(maxBaseline, offset.Value);
                    maxBaselineTail = Math.Max(maxBaselineTail,
                        item.Box.ActualBoxSizingHeight - offset.Value + item.Box.ActualMarginBottom);
                }
            }

            foreach (var item in line.Items)
            {
                var align = item.Box.AlignSelf is "auto" or "" ? _flexBox.AlignItems : item.Box.AlignSelf;
                double crossMarginBefore = _isRow ? item.Box.ActualMarginTop    : item.Box.ActualMarginLeft;
                double crossMarginAfter  = _isRow ? item.Box.ActualMarginBottom : item.Box.ActualMarginRight;
                double itemCrossSize = _isRow ? item.Box.ActualBoxSizingHeight : item.Box.ActualBoxSizingWidth;

                // `flex-wrap: wrap-reverse` swaps the cross-start and cross-end directions
                // (https://www.w3.org/TR/css-flexbox-1/#flex-wrap-property), and that swap applies inside a
                // line as much as it does to the stack of lines: "flush with the line's cross-start edge"
                // (§8.3) names the *bottom* of a row line under wrap-reverse, and cross-end names its top.
                // So the two flush arms exchange places, while everything else here is unaffected — center
                // is symmetric even with unequal cross margins (the margin box is centred either way), and
                // an item that really stretches fills the line and is on both edges at once.
                //
                // This is a different reversal from the one DistributeCrossSpace applies, which stacks the
                // *lines* the other way round. They compose; applying either one twice cancels it.
                double flushCrossStart = _isWrapReverse
                    ? line.CrossSize - itemCrossSize - crossMarginAfter
                    : crossMarginBefore;
                double flushCrossEnd = _isWrapReverse
                    ? crossMarginBefore
                    : line.CrossSize - itemCrossSize - crossMarginAfter;

                switch (align)
                {
                    case CssConstants.FlexEnd:
                    case "end":
                        item.CrossOffset = flushCrossEnd;
                        break;
                    case CssConstants.Center:
                        item.CrossOffset = (line.CrossSize - itemCrossSize - crossMarginBefore - crossMarginAfter) / 2
                                         + crossMarginBefore;
                        break;
                    case CssConstants.Stretch:
                    case "normal":
                    {
                        bool canStretch = _isRow
                            ? !CssValueParser.IsValidLength(item.Box.Height)
                            : !CssValueParser.IsValidLength(item.Box.Width);
                        if (canStretch)
                        {
                            double targetCross = line.CrossSize - crossMarginBefore - crossMarginAfter;
                            double currentCross = _isRow ? item.Box.ActualBoxSizingHeight : item.Box.ActualBoxSizingWidth;
                            if (Math.Abs(targetCross - currentCross) > 0.5)
                            {
                                if (_isRow)
                                {
                                    var savedHeight = item.Box.Height;
                                    var savedWidth  = item.Box.Width;
                                    // Cross-axis stretch: set explicit Height for the re-layout but also
                                    // lock the main-axis Width so GetBoxWidth can't fall back to container fill.
                                    double crossContent = Math.Max(0, targetCross - item.Box.ActualPaddingTop - item.Box.ActualPaddingBottom
                                                                                  - item.Box.ActualBorderTopWidth - item.Box.ActualBorderBottomWidth);
                                    item.Box.Height = FormatLayoutUnits(crossContent);
                                    item.Box.Width  = FormatLayoutUnits(Math.Max(0, item.FinalMainSize - MainPaddingBorder(item.Box)));
                                    item.Box.Location = new RPoint(_flexBox.ClientLeft, _flexBox.ClientTop);
                                    item.Box.ActualBottom = item.Box.Location.Y;
                                    item.Box.RectanglesReset();
                                    await PerformLayoutBlockified(g, item.Box);
                                    item.Box.Height = savedHeight;
                                    item.Box.Width  = savedWidth;
                                }
                                else
                                {
                                    var savedWidth  = item.Box.Width;
                                    var savedHeight = item.Box.Height;
                                    // Column direction: lock cross Width and preserve main-axis Height.
                                    double crossContent = Math.Max(0, targetCross - item.Box.ActualPaddingLeft - item.Box.ActualPaddingRight
                                                                                  - item.Box.ActualBorderLeftWidth - item.Box.ActualBorderRightWidth);
                                    item.Box.Width  = FormatLayoutUnits(crossContent);
                                    item.Box.Height = FormatLayoutUnits(Math.Max(0, item.FinalMainSize - MainPaddingBorder(item.Box)));
                                    item.Box.Location = new RPoint(_flexBox.ClientLeft, _flexBox.ClientTop);
                                    item.Box.ActualBottom = item.Box.Location.Y;
                                    item.Box.RectanglesReset();
                                    await PerformLayoutBlockified(g, item.Box);
                                    item.Box.Width  = savedWidth;
                                    item.Box.Height = savedHeight;
                                }
                            }
                        }
                        // An item that could not stretch (it has a definite cross size) falls back to
                        // flex-start, so it takes the swapped edge with it — that fallback, not an explicit
                        // `align-items: flex-start`, is the common way a wrap-reverse container reaches here.
                        // The cross size is re-read rather than reused from above: the branch that just ran
                        // may have re-laid the item out at the line's cross size, and the swapped edge is
                        // measured against the size the item actually ended up with.
                        //
                        // An item that does fill its line is on both edges at once, and its re-laid-out size
                        // goes through a string round-trip on the way (FormatLayoutUnits), so subtracting it
                        // back off the line's own cross size does not land exactly on the margin it started
                        // from. Below the same 0.5 tolerance the re-layout itself uses, take the margin —
                        // otherwise a stretched item moves by ~1e-4 and redraws its whole border.
                        double stretchedCross = _isRow ? item.Box.ActualBoxSizingHeight : item.Box.ActualBoxSizingWidth;
                        double stretchedCrossStart = line.CrossSize - stretchedCross - crossMarginAfter;
                        item.CrossOffset = _isWrapReverse && stretchedCrossStart - crossMarginBefore > 0.5
                            ? stretchedCrossStart
                            : crossMarginBefore;
                        break;
                    }
                    case CssConstants.Baseline when baselineOffsets != null && baselineOffsets.TryGetValue(item, out var itemBaseline):
                        // The group's baselines align with each other either way; what the swap changes is
                        // which end of the line the group is flushed against.
                        item.CrossOffset = _isWrapReverse
                            ? line.CrossSize - maxBaselineTail - itemBaseline
                            : crossMarginBefore + (maxBaseline - itemBaseline);
                        break;
                    default: // flex-start / start / baseline fallback (column-direction, or no discoverable baseline)
                        item.CrossOffset = flushCrossStart;
                        break;
                }
            }
        }

        // ─── Phase 9: final locations ─────────────────────────────────────────────

        private void AssignLocations(List<FlexLine> lines)
        {
            double containerMainStart = _isRow ? _flexBox.ClientLeft : _flexBox.ClientTop;
            double containerMainEnd   = _isRow ? _flexBox.ClientRight : _flexBox.ClientBottom;
            double containerCrossStart = _isRow ? _flexBox.ClientTop : _flexBox.ClientLeft;

            foreach (var line in lines)
            {
                foreach (var item in line.Items)
                {
                    double mainPos = _isReverse
                        ? containerMainEnd - item.MainOffset - item.FinalMainSize
                        : containerMainStart + item.MainOffset;

                    double crossPos = containerCrossStart + line.CrossOffset + item.CrossOffset;

                    double targetX = _isRow ? mainPos : crossPos;
                    double targetY = _isRow ? crossPos : mainPos;

                    double dx = targetX - item.Box.Location.X;
                    double dy = targetY - item.Box.Location.Y;

                    if (Math.Abs(dx) > 0.01) item.Box.OffsetLeft(dx);
                    if (Math.Abs(dy) > 0.01) item.Box.OffsetTop(dy);
                }
            }
        }

        // ─── Phase 9b: fragmentation ──────────────────────────────────────────────

        /// <summary>
        /// Moves a flex line onto the next fragmentainer where it would otherwise be cut by the boundary,
        /// and honours the break values declared on its items.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>This is the pass this engine did not have.</b> Every earlier phase lays an item out at the
        /// container's content origin purely to <i>measure</i> it, and
        /// <see cref="AssignLocations"/> translates it into place afterwards — so a break decision taken
        /// during any of them names a position the item is about to be moved away from, which is why they
        /// run with breaking suppressed. Only here are the items where they will finally be, and only
        /// here can the question "does this fit in the fragmentainer?" be asked of the right coordinates.
        /// </para>
        /// <para>
        /// A <b>flex line</b> is what moves, not an item. The line is the row of items laid out together;
        /// moving one of them alone would break the alignment the cross-axis phases just established, and
        /// <see href="https://www.w3.org/TR/css-break-3/#break-between">§3.1</see>'s break points in a
        /// flex container are between lines rather than between the items sharing one. In a single-line
        /// container that is every item at once, which is the right answer there too: they are aligned
        /// against each other.
        /// </para>
        /// <para>
        /// The line is <i>translated</i> rather than laid out again, which is the choice
        /// <see href="https://github.com/jhaygood86/PeachPDF/issues/332">#332</see> made the other way for
        /// block flow. It has to be: an item's position is not derived from its own layout but assigned by
        /// the engine, so re-flowing it at a new position would change the measurement the container's own
        /// size was computed from. The cost is the one #332 documents — an item whose text had already
        /// crossed the boundary carries the gap with it — and it is why an item that does not fit in a
        /// fragmentainer at all is left alone rather than moved forever.
        /// </para>
        /// </remarks>
        private void RelocateLinesAcrossFragmentainers(List<FlexLine> lines)
        {
            var container = _flexBox.HtmlContainer;

            // Only where breaking is actually live. Inside a table cell, or during another engine's
            // measurement, this container's own coordinates are provisional and belong to that engine's
            // grid - shifting a line against the *page* grid from here moves it out from under the row
            // that is placing it, which is the defect the monolithic mover had to be gated for too.
            // An inline-flex is excluded on the same grounds: it is an atomic inline, and where it sits
            // is the line it is on to decide.
            if (container is null || !container.HasRealPageGrid || lines.Count == 0
                || !container.IsFragmenting
                || _flexBox.Display == CssConstants.InlineFlex)
            {
                return;
            }

            var flowLines = new List<IReadOnlyList<CssBox>>(lines.Count);

            foreach (var line in lines)
            {
                if (line.Items.Count == 0) continue;

                flowLines.Add(line.Items.Select(item => item.Box).ToList());
            }

            if (flowLines.Count == 0) return;

            var shift = LineRelocation.Relocate(container, BuildLineGroups(flowLines));

            if (shift > 0)
            {
                _flexBox.ActualBottom += shift;
            }
        }

        /// <summary>
        /// Lays each item of a single, row-direction line's content out for real, at the position it now
        /// finally holds, with breaking genuinely live — rather than translating an already-measured
        /// subtree the way every earlier phase does. Publishes this container's own
        /// <see cref="FlexBreakToken"/> when one or more items did not finish.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>Why this is safe only now.</b> Every phase before <see cref="RelocateLinesAcrossFragmentainers"/>
        /// measures an item at the container's content origin and is about to move it — a break decision
        /// taken there names a position the item is about to leave, which is why <see cref="PerformLayoutBlockified"/>
        /// runs those with the fragmentainer detached. By the time this runs, <see cref="AssignLocations"/>
        /// and the line relocation above have already put every item exactly where it will stay, so asking
        /// "does this content fit here?" finally asks the right coordinates — the same thing an ordinary
        /// page pass, or a table cell's own resumed content pass, already does.
        /// </para>
        /// <para>
        /// <b>Row-direction, single line, only.</b> A row line's items sit side by side sharing one
        /// block-axis range, which is exactly css-break-3 §2.1's "parallel flows" — the spec's own example
        /// of the concept is "the contents of each flex item in a flex layout row" — so this is the direct
        /// analogue of a table row's cells (<c>TableRowCursor</c>/<see cref="TableBreakToken"/>), and
        /// <see cref="FlexBreakToken"/> mirrors that shape for the same reason. A column-direction line's
        /// items are sequential rather than parallel (a different unit of work — see
        /// <c>flex-column-container-has-no-break-points-between-items.md</c>'s equivalent scope cut for the
        /// break-value half of this problem), and a wrapped container's second line has no record to resume
        /// it from yet; both are left at today's translate-only behaviour rather than half-built.
        /// </para>
        /// </remarks>
        private async ValueTask CommitItemContent(RGraphics g, List<FlexLine> lines)
        {
            var container = _flexBox.HtmlContainer;

            // The same liveness gate RelocateLinesAcrossFragmentainers uses, for the same reason: outside
            // it this container's own coordinates are provisional, belonging to whatever is measuring it.
            if (container is null || !container.HasRealPageGrid || !container.IsFragmenting
                || _flexBox.Display == CssConstants.InlineFlex || !_isRow || lines.Count != 1)
            {
                return;
            }

            var line = lines[0];
            if (line.Items.Count == 0) return;

            var unfinished = new List<UnfinishedFlexItem>();
            var finished = new List<CssBox>();

            foreach (var item in line.Items)
            {
                await PerformCommitLayout(g, item.Box, resume: null);

                if (item.Box.PendingBreakToken is { } token)
                    unfinished.Add(new UnfinishedFlexItem(item.Box, token));
                else
                    finished.Add(item.Box);
            }

            if (unfinished.Count > 0)
            {
                var resumeSlot = unfinished.Max(u => u.Token.ResumeSlotIndex);
                _flexBox.SetPendingBreakToken(new FlexBreakToken(_flexBox, resumeSlot, unfinished, finished));
            }
        }

        /// <summary>
        /// Re-enters exactly the items an earlier pass's <see cref="FlexBreakToken"/> named as unfinished,
        /// at the position they already hold, and continues their content from where each one stopped.
        /// </summary>
        /// <remarks>
        /// Nothing else about the container is touched: every phase that decides sizing, line membership
        /// and position already ran on an earlier pass and its results are sitting on the live box tree
        /// untouched (nothing between passes resets an item's <see cref="CssBox.Location"/> or
        /// its pinned content-box <c>Width</c>/<c>Height</c> — see <see cref="PerformCommitLayout"/>), which
        /// is what a resumed pass is allowed to rely on and a fresh one is not.
        /// </remarks>
        private async ValueTask ResumeCommitPass(RGraphics g, FlexBreakToken resume)
        {
            var stillUnfinished = new List<UnfinishedFlexItem>();

            foreach (var unfinishedItem in resume.UnfinishedItems)
            {
                await PerformCommitLayout(g, unfinishedItem.Item, unfinishedItem.Token);

                if (unfinishedItem.Item.PendingBreakToken is { } token)
                    stillUnfinished.Add(new UnfinishedFlexItem(unfinishedItem.Item, token));
            }

            if (stillUnfinished.Count > 0)
            {
                var nowFinished = resume.FinishedItems.Concat(
                    resume.UnfinishedItems
                        .Select(u => u.Item)
                        .Where(item => stillUnfinished.All(u => !ReferenceEquals(u.Item, item))))
                    .ToList();

                var resumeSlot = stillUnfinished.Max(u => u.Token.ResumeSlotIndex);
                _flexBox.SetPendingBreakToken(new FlexBreakToken(_flexBox, resumeSlot, stillUnfinished, nowFinished));
            }
        }

        /// <summary>
        /// Lays <paramref name="box"/>'s own content out, attached to a real fragmentainer rather than a
        /// detached one — the one place in this engine breaking is genuinely live for an item's content.
        /// </summary>
        /// <param name="g">the graphics context layout is running against</param>
        /// <param name="box">the item to lay out</param>
        /// <param name="resume">
        /// the item's own break token from an earlier pass, or null to lay it out from the start.
        /// </param>
        /// <remarks>
        /// <para>
        /// A <b>fresh</b> commit (<paramref name="resume"/> null) pins <paramref name="box"/>'s content-box
        /// <c>Width</c>/<c>Height</c> to its already-resolved outer size
        /// (<see cref="CssBox.ActualBoxSizingWidth"/>/<see cref="CssBox.ActualBoxSizingHeight"/>)
        /// before laying out — every earlier phase already decided this item's size, and re-deriving it from
        /// an "auto" property here (the value every earlier phase temporarily sets and then reverts, since
        /// none of them are the item's <i>final</i> layout) would let this, genuinely final, layout disagree
        /// with the size the rest of the flex algorithm already committed to. Unlike those earlier phases,
        /// this pin is <b>not</b> reverted afterward: a later fragmentainer pass resuming this same item
        /// (<paramref name="resume"/> non-null) must see the same <c>Width</c>/<c>Height</c> the first pass
        /// used, or a nested engine that re-derives its own content box from them
        /// (<c>CssLayoutEngineColumns.Layout</c>'s <c>containerWidth</c>) would size itself differently
        /// pass to pass. <see cref="CssBox.RectanglesReset"/> only runs on the fresh path too, for the same
        /// reason a resumed table cell's continuation must not call it — see
        /// <c>CssBox.PerformLayoutPrologue</c>'s own remarks: it would discard geometry an earlier
        /// fragmentainer has already frozen a fragment around.
        /// </para>
        /// <para>
        /// A <b>resumed</b> commit (<paramref name="resume"/> non-null) instead calls
        /// <see cref="CssBox.ResumeAt"/> — the same primitive a table row loop uses to re-enter a cell mid
        /// content — and touches nothing else.
        /// </para>
        /// </remarks>
        private static async ValueTask PerformCommitLayout(RGraphics g, CssBox box, BreakToken? resume)
        {
            if (resume is null)
            {
                var horizontalPB = box.ActualPaddingLeft + box.ActualPaddingRight
                    + box.ActualBorderLeftWidth + box.ActualBorderRightWidth;
                var verticalPB = box.ActualPaddingTop + box.ActualPaddingBottom
                    + box.ActualBorderTopWidth + box.ActualBorderBottomWidth;

                box.Width = FormatLayoutUnits(Math.Max(0, box.ActualBoxSizingWidth - horizontalPB));
                box.Height = FormatLayoutUnits(Math.Max(0, box.ActualBoxSizingHeight - verticalPB));

                box.RectanglesReset();
            }
            else
            {
                box.ResumeAt(resume, resumeTopOverride: null);
            }

            // Every earlier item layout in this engine is a measurement, translated into place afterward -
            // PlaceBlockChild running during one of those is harmless, since AssignLocations overwrites its
            // result unconditionally. This is the item's real, final content layout, with nothing after it
            // to correct a wrong position back, so LayoutContents must not let PlaceBlockChild touch it.
            box.PositionAssignedByEngine = true;
            try
            {
                await PerformLayoutBlockifiedAtFinalPosition(g, box);
            }
            finally
            {
                box.PositionAssignedByEngine = false;
            }
        }

        /// <summary>
        /// The commit pass's own version of <see cref="PerformLayoutBlockified"/>: the same blockify
        /// dance (CSS Display 3 §2.3's flex-item requirement), but without detaching the fragmentainer or
        /// suppressing word-level breaking — this is the one item layout in this engine that runs at the
        /// item's real, final position, so breaking questions asked during it are meaningful.
        /// </summary>
        private static async ValueTask PerformLayoutBlockifiedAtFinalPosition(RGraphics g, CssBox box)
        {
            string? savedDisplay = null;
            if (box.IsInline)
            {
                savedDisplay = box.Display;
                box.Display = CssConstants.Block;
            }

            await box.PerformLayout(g);

            if (savedDisplay != null)
                box.Display = savedDisplay;
        }

        /// <summary>
        /// The container's lines as <see cref="LineGroup"/>s in <b>block-axis</b> order, each carrying the
        /// two sides of the break point immediately above it in <b>flow</b> order.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Neither order is the order <paramref name="flowLines"/> is in. Flex lines are laid out in flow
        /// order, but where they sit is decided by the cross axis, and two ordinary declarations put that
        /// somewhere other than "one below the next".
        /// </para>
        /// <para>
        /// <b><c>flex-wrap: wrap-reverse</c></b> stacks the lines in the opposite cross-axis direction
        /// (<see cref="DistributeCrossSpace"/>), so the first line in flow is the <i>last</i> one down the
        /// page. The walk needs it the other way round — a displacement accumulates onto the lines
        /// <i>below</i> the one that moved, and a fragmentainer boundary leaves a particular line above
        /// it. The break point's two sides stay in flow order regardless, because that is how
        /// <see href="https://www.w3.org/TR/css-break-3/#break-between">§3.1</see> identifies one: the
        /// earlier sibling's <c>break-after</c> and the later one's <c>break-before</c> name the same
        /// point, which here is the boundary <i>above the earlier line</i>, so it is the earlier line
        /// that begins the new fragmentainer.
        /// </para>
        /// <para>
        /// <b>A column direction</b> stacks the lines along the <i>inline</i> axis instead: they sit side
        /// by side sharing one block-axis range, so no fragmentainer boundary falls between two of them
        /// and a break value there names nothing this pass can act on. All of them are one group, which
        /// keeps the geometric half — a line holding something that may not be cut still moves, and the
        /// lines beside it move with it rather than sliding out of alignment. Their block-axis break
        /// points are the ones <i>between items within</i> a line, which this pass does not have.
        /// </para>
        /// </remarks>
        /// <param name="flowLines">the container's non-empty lines, in flow order</param>
        private List<LineGroup> BuildLineGroups(List<IReadOnlyList<CssBox>> flowLines)
        {
            if (!_isRow)
            {
                // The break point before the container's first in-flow child is the one before the
                // container itself (§3.1), and that boundary *is* above this group - so that one child
                // speaks for it. Only that one: the rest of its line is stacked *below* it in the block
                // axis, so a break-before there names a boundary inside the container, which is the
                // break point this arm does not take. Reading the whole line would move content that
                // sits before the break point along with it.
                //
                // A column container that did not wrap has one line, and with a single item comes out
                // of this identical to the walk below, which is why the arm needs no line count.
                return
                [
                    new LineGroup(flowLines.SelectMany(line => line).ToList(), null, [flowLines[0][0]])
                ];
            }

            var groups = new List<LineGroup>(flowLines.Count);

            // `index` is the position down the page; `flowIndex` is the position in the source. They are
            // the same list read in opposite directions under wrap-reverse, and the same list otherwise.
            for (var index = 0; index < flowLines.Count; index++)
            {
                var flowIndex = _isWrapReverse ? flowLines.Count - 1 - index : index;
                var boxes = flowLines[flowIndex];

                IReadOnlyList<CssBox>? earlier;
                IReadOnlyList<CssBox>? later;

                if (index == 0)
                {
                    // Nothing sits above the first line down the page but the container's own top edge,
                    // and the break point there is §3.1's before the container's first in-flow child —
                    // which is the break point before the *container*. So the first line **in flow**
                    // speaks for it however the lines are stacked: forcing it moves this line, and the
                    // accumulation carries the rest with it, which is the container starting a new page.
                    // Reading this group's own break-before instead would drop the declaration entirely
                    // under wrap-reverse, where the first line in flow is the last one down the page.
                    earlier = null;
                    later = flowLines[0];
                }
                else if (_isWrapReverse)
                {
                    earlier = boxes;
                    later = flowLines[flowIndex + 1];
                }
                else
                {
                    earlier = flowLines[flowIndex - 1];
                    later = boxes;
                }

                groups.Add(new LineGroup(boxes, earlier, later));
            }

            return groups;
        }

        // ─── Direction / wrap parsing ─────────────────────────────────────────────

        private void ParseFlexDirection()
        {
            switch (_flexBox.FlexDirection)
            {
                case CssConstants.RowReverse:
                    _isRow = true;  _isReverse = true;  break;
                case CssConstants.Column:
                    _isRow = false; _isReverse = false; break;
                case CssConstants.ColumnReverse:
                    _isRow = false; _isReverse = true;  break;
                default: // row
                    _isRow = true;  _isReverse = false; break;
            }
        }

        private void ParseFlexWrap()
        {
            switch (_flexBox.FlexWrap)
            {
                case "wrap":
                    _isWrap = true;  _isWrapReverse = false; break;
                case CssConstants.WrapReverse:
                    _isWrap = true;  _isWrapReverse = true;  break;
                default: // nowrap
                    _isWrap = false; _isWrapReverse = false; break;
            }
        }

        // ─── Axis helpers ─────────────────────────────────────────────────────────

        private double MainMarginBefore(CssBox box) =>
            _isRow ? box.ActualMarginLeft : box.ActualMarginTop;

        private double MainMarginAfter(CssBox box) =>
            _isRow ? box.ActualMarginRight : box.ActualMarginBottom;

        private bool IsMainMarginBeforeAuto(CssBox box) =>
            (_isRow ? box.MarginLeft : box.MarginTop) == CssConstants.Auto;

        private bool IsMainMarginAfterAuto(CssBox box) =>
            (_isRow ? box.MarginRight : box.MarginBottom) == CssConstants.Auto;

        private double MainPaddingBorder(CssBox box) =>
            _isRow
                ? box.ActualPaddingLeft + box.ActualPaddingRight + box.ActualBorderLeftWidth  + box.ActualBorderRightWidth
                : box.ActualPaddingTop  + box.ActualPaddingBottom + box.ActualBorderTopWidth + box.ActualBorderBottomWidth;

        // Clamps an outer main-axis size against the item's min/max constraints for the main axis
        // (min/max-width for row, min/max-height for column). Per spec, min wins over max on conflict.
        private double ClampMainAxis(CssBox box, double outerSize, double mainSize)
        {
            var maxRaw = _isRow ? box.MaxWidth : box.MaxHeight;
            if (CssValueParser.IsValidLength(maxRaw))
                outerSize = Math.Min(outerSize, CssValueParser.ParseLength(maxRaw, mainSize, box) + MainPaddingBorder(box));

            var minRaw = _isRow ? box.MinWidth : box.MinHeight;
            if (CssValueParser.IsValidLength(minRaw))
                outerSize = Math.Max(outerSize, CssValueParser.ParseLength(minRaw, mainSize, box) + MainPaddingBorder(box));

            return outerSize;
        }

        // ─── Gap helpers ──────────────────────────────────────────────────────────

        // column-gap = between items in a row direction (main axis gap for row flex)
        // row-gap    = between items in a column direction (main axis gap for column flex)
        private double ParseMainGap(double mainSize) =>
            CssValueParser.ParseLength(
                _isRow ? _flexBox.FlexColumnGap : _flexBox.FlexRowGap,
                mainSize, _flexBox);

        private double ParseCrossGap(double mainSize) =>
            CssValueParser.ParseLength(
                _isRow ? _flexBox.FlexRowGap : _flexBox.FlexColumnGap,
                mainSize, _flexBox);

        // ─── Layout helpers ───────────────────────────────────────────────────────

        /// <summary>
        /// Runs PerformLayout on a flex item, temporarily blockifying it when its computed
        /// display is inline. CSS spec §9.2 requires flex items to be blockified, so that
        /// block-layout sizing (CreateLineBoxes, explicit width/height) works correctly. Every
        /// caller in this class uses this purely to measure/re-measure natural or constrained
        /// content size at a provisional position (<c>(_flexBox.ClientLeft, _flexBox.ClientTop)</c>
        /// - whatever <paramref name="box"/>'s <c>Location</c> currently holds), never as the
        /// item's real final placement (<see cref="AssignLocations"/> always translates it
        /// afterward). Suppresses the per-word page-break-avoidance check for the duration - see
        /// <see cref="HtmlContainerInt.SuppressWordPageBreaks"/> for why a page-break decision made
        /// against this provisional position must never be allowed to stick.
        /// </summary>
        private static async ValueTask PerformLayoutBlockified(RGraphics g, CssBox box)
        {
            string? savedDisplay = null;
            if (box.IsInline)
            {
                savedDisplay = box.Display;
                box.Display  = CssConstants.Block;
            }

            var container = box.HtmlContainer;
            var previousSuppress = container?.SuppressWordPageBreaks ?? false;
            if (container is not null)
                container.SuppressWordPageBreaks = true;

            // A break token recorded here would name a position this item is about to be translated away
            // from, so this scope suppresses breaking as well as the legacy word relocation above. The two
            // used to be one flag, which meant breaking could never be enabled for a placed item without
            // also re-enabling the relocation - see FragmentainerContext.IsFragmenting.
            var fragmentainer = container?.DetachFragmentainer();

            try
            {
                await box.PerformLayout(g);
            }
            finally
            {
                container?.RestoreFragmentainer(fragmentainer);

                if (container is not null)
                    container.SuppressWordPageBreaks = previousSuppress;
            }

            if (savedDisplay != null)
                box.Display = savedDisplay;
        }

        // ─── Value helpers ────────────────────────────────────────────────────────

        private static int ParseOrder(CssBox box) =>
            int.TryParse(box.Order, out var o) ? o : 0;

        private static float ParseFloat(string val) =>
            float.TryParse(val, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var f) ? f : 0f;

        // Format a resolved size back into a length string that ParseLength returns 1:1. The values
        // here are already in internal layout units (points), so serialize as "pt" (the identity
        // unit) - NOT "px", which now resolves at the spec-correct 0.75pt and would silently shrink
        // every re-parsed flex size to 75% of the value computed here.
        private static string FormatLayoutUnits(double value) =>
            value.ToString("F4", System.Globalization.CultureInfo.InvariantCulture) + "pt";

        // ─── Data classes ─────────────────────────────────────────────────────────

        private sealed class FlexItem(CssBox box, double naturalMainSize, double hypotheticalMainSize)
        {
            public CssBox  Box                 { get; } = box;
            public double  NaturalMainSize      { get; } = naturalMainSize;
            public double  HypotheticalMainSize { get; } = hypotheticalMainSize;
            public double  FinalMainSize        { get; set; } = hypotheticalMainSize;
            public double  MainOffset           { get; set; }
            public double  CrossOffset          { get; set; }

            // Resolved main-axis margins: 0 for an "auto" margin until Phase 7 distributes
            // free space into it (spec §8.1); otherwise the item's actual parsed margin.
            public bool    MarginBeforeAuto     { get; set; }
            public bool    MarginAfterAuto      { get; set; }
            public double  MarginBefore         { get; set; }
            public double  MarginAfter          { get; set; }
        }

        private sealed class FlexLine(List<FlexItem> items)
        {
            public List<FlexItem> Items       { get; } = items;
            public double         CrossSize   { get; set; }
            public double         CrossOffset { get; set; }
        }
    }
}
