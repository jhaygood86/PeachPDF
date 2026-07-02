using PeachPDF;
using PeachPDF.Html.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core.Entities;
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

        public static async ValueTask PerformLayout(RGraphics g, CssBox flexBox)
        {
            try
            {
                await new CssLayoutEngineFlex(flexBox).Layout(g);
            }
            catch (Exception ex)
            {
                flexBox.HtmlContainer?.ReportError(HtmlRenderErrorType.Layout, "Failed flex layout", ex);
            }
        }

        private async ValueTask Layout(RGraphics g)
        {
            ParseFlexDirection();
            ParseFlexWrap();

            // Compute container main-axis size
            var containerWidth = await CssLayoutEngine.GetBoxWidth(g, _flexBox);
            _flexBox.ActualRight = _flexBox.Location.X + containerWidth + _flexBox.ActualBoxSizeIncludedWidth;
            _flexBox.ActualBottom = _flexBox.Location.Y;

            // Pre-apply explicit height when set, so ClientBottom is correct for cross-axis sizing
            bool hasExplicitHeight = CssValueParser.IsValidLength(_flexBox.Height);
            if (hasExplicitHeight)
            {
                var fullHeight = CssLayoutEngine.GetBoxHeight(_flexBox) ?? 0;
                _flexBox.ActualBottom = _flexBox.Location.Y + fullHeight;
            }

            double mainSize = _isRow
                ? _flexBox.ClientRight - _flexBox.ClientLeft
                : (_flexBox.ClientBottom - _flexBox.ClientTop);

            // Container cross-axis size (0 when auto/unknown until content lays out)
            double containerCrossSize = _isRow
                ? (hasExplicitHeight ? _flexBox.ClientBottom - _flexBox.ClientTop : 0)
                : containerWidth;

            // Whether the main axis has indefinite size (column + auto height = no grow/shrink)
            bool mainSizeIndefinite = !_isRow && !hasExplicitHeight;

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
                if (!hasExplicitHeight)
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
            DistributeCrossSpace(lines, crossFree, crossGap);

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

            if (!hasExplicitHeight)
            {
                if (_isRow)
                    _flexBox.ActualBottom = _flexBox.ClientTop + maxCrossEnd
                        + _flexBox.ActualPaddingBottom + _flexBox.ActualBorderBottomWidth;
                else
                    _flexBox.ActualRight = _flexBox.ClientLeft + maxCrossEnd
                        + _flexBox.ActualPaddingRight + _flexBox.ActualBorderRightWidth;
            }

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
                if (_isRow) { savedDim = box.Width;  box.Width  = FormatPx(cssContentSize); }
                else        { savedDim = box.Height; box.Height = FormatPx(cssContentSize); }
            }

            box.Location = new RPoint(_flexBox.ClientLeft, _flexBox.ClientTop);
            box.ActualBottom = box.Location.Y;
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
                item.Box.Width = FormatPx(cssContentSize);
            }
            else
            {
                saved = item.Box.Height;
                item.Box.Height = FormatPx(cssContentSize);
            }

            item.Box.Location = new RPoint(_flexBox.ClientLeft, _flexBox.ClientTop);
            item.Box.ActualBottom = item.Box.Location.Y;
            item.Box.RectanglesReset();
            await PerformLayoutBlockified(g, item.Box);

            if (_isRow) item.Box.Width  = saved;
            else        item.Box.Height = saved;
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

        private void DistributeCrossSpace(List<FlexLine> lines, double remaining, double crossGap)
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

            if (_isWrapReverse)
            {
                // Reverse line stacking: exchange the cross offset computed for line[i] with
                // the offset computed for line[n-1-i]. This correctly handles any align-content
                // value (including ones with non-uniform spacing) and preserves inter-line gaps.
                var reversedOffsets = lines.Select(l => l.CrossOffset).Reverse().ToList();
                for (int i = 0; i < lines.Count; i++)
                    lines[i].CrossOffset = reversedOffsets[i];
            }
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
            Dictionary<FlexItem, double>? baselineOffsets = null;
            if (_isRow)
            {
                foreach (var item in line.Items)
                {
                    var itemAlign = item.Box.AlignSelf is "auto" or "" ? _flexBox.AlignItems : item.Box.AlignSelf;
                    if (itemAlign != CssConstants.Baseline) continue;

                    var offset = GetItemBaselineOffset(item.Box);
                    if (offset is null) continue;

                    baselineOffsets ??= [];
                    baselineOffsets[item] = offset.Value;
                    maxBaseline = Math.Max(maxBaseline, offset.Value);
                }
            }

            foreach (var item in line.Items)
            {
                var align = item.Box.AlignSelf is "auto" or "" ? _flexBox.AlignItems : item.Box.AlignSelf;
                double crossMarginBefore = _isRow ? item.Box.ActualMarginTop    : item.Box.ActualMarginLeft;
                double crossMarginAfter  = _isRow ? item.Box.ActualMarginBottom : item.Box.ActualMarginRight;
                double itemCrossSize = _isRow ? item.Box.ActualBoxSizingHeight : item.Box.ActualBoxSizingWidth;

                switch (align)
                {
                    case CssConstants.FlexEnd:
                    case "end":
                        item.CrossOffset = line.CrossSize - itemCrossSize - crossMarginAfter;
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
                                    item.Box.Height = FormatPx(crossContent);
                                    item.Box.Width  = FormatPx(Math.Max(0, item.FinalMainSize - MainPaddingBorder(item.Box)));
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
                                    item.Box.Width  = FormatPx(crossContent);
                                    item.Box.Height = FormatPx(Math.Max(0, item.FinalMainSize - MainPaddingBorder(item.Box)));
                                    item.Box.Location = new RPoint(_flexBox.ClientLeft, _flexBox.ClientTop);
                                    item.Box.ActualBottom = item.Box.Location.Y;
                                    item.Box.RectanglesReset();
                                    await PerformLayoutBlockified(g, item.Box);
                                    item.Box.Width  = savedWidth;
                                    item.Box.Height = savedHeight;
                                }
                            }
                        }
                        item.CrossOffset = crossMarginBefore;
                        break;
                    }
                    case CssConstants.Baseline when baselineOffsets != null && baselineOffsets.TryGetValue(item, out var itemBaseline):
                        item.CrossOffset = crossMarginBefore + (maxBaseline - itemBaseline);
                        break;
                    default: // flex-start / start / baseline fallback (column-direction, or no discoverable baseline)
                        item.CrossOffset = crossMarginBefore;
                        break;
                }
            }
        }

        /// <summary>
        /// Finds the first line box within <paramref name="box"/>'s subtree, in document order
        /// (depth-first, skipping out-of-flow and display:none descendants), per CSS Flexbox
        /// §8.5's "first in-flow child" baseline search.
        /// </summary>
        private static CssLineBox? FindFirstLineBox(CssBox box)
        {
            if (box.LineBoxes.Count > 0)
                return box.LineBoxes[0];

            foreach (var child in box.Boxes)
            {
                if (child.Display == CssConstants.None || child.IsOutOfFlow) continue;

                var found = FindFirstLineBox(child);
                if (found != null) return found;
            }

            return null;
        }

        /// <summary>
        /// Computes a flex item's baseline offset from its own cross-start (top) edge — the
        /// first font baseline of its content, per CSS Flexbox §8.5. Returns null if the item
        /// has no line-box content anywhere, in which case it falls back to flex-start.
        /// </summary>
        private static double? GetItemBaselineOffset(CssBox box)
        {
            var lineBox = FindFirstLineBox(box);
            if (lineBox == null) return null;

            var word = CssBox.FirstWordOccurence(lineBox.OwnerBox, lineBox);
            if (word == null) return null;

            return (word.Top - box.Location.Y) + word.OwnerBox.ActualFont.Ascent;
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
        /// display is inline.  CSS spec §9.2 requires flex items to be blockified, so that
        /// block-layout sizing (CreateLineBoxes, explicit width/height) works correctly.
        /// </summary>
        private static async ValueTask PerformLayoutBlockified(RGraphics g, CssBox box)
        {
            string? savedDisplay = null;
            if (box.IsInline)
            {
                savedDisplay = box.Display;
                box.Display  = CssConstants.Block;
            }

            await box.PerformLayout(g);

            if (savedDisplay != null)
                box.Display = savedDisplay;
        }

        // ─── Value helpers ────────────────────────────────────────────────────────

        private static int ParseOrder(CssBox box) =>
            int.TryParse(box.Order, out var o) ? o : 0;

        private static float ParseFloat(string val) =>
            float.TryParse(val, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var f) ? f : 0f;

        // Format in internal layout units as "px" so ParseLength returns the value 1:1 (px factor = 1).
        private static string FormatPx(double value) =>
            value.ToString("F4", System.Globalization.CultureInfo.InvariantCulture) + "px";

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
