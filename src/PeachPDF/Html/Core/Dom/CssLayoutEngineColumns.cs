using PeachPDF.Html.Adapters;
using PeachPDF.Html.Core.Entities;
using PeachPDF.Html.Core.Parse;
using PeachPDF.Html.Core.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PeachPDF.Html.Core.Dom
{
    /// <summary>
    /// Lays out a CSS Multi-column Layout container (<c>column-count</c>/<c>column-width</c>/<c>columns</c>).
    ///
    /// v1 scope: children are laid out once as a single tall flow at the resolved column width (reusing
    /// ordinary block layout unchanged), then whole top-level children are reassigned — atomically, never
    /// split — to (page, column) slots by height, and moved into place. This means a single child (e.g. one
    /// paragraph) never itself splits across a column or page boundary the way plain block content already
    /// does elsewhere in this engine (which relies on the paint phase's per-page clipping, not real
    /// fragmentation, to split a tall paragraph's lines across pages) — only a whole child moves as a unit.
    /// For content made of many short block children (the common real-world case: dictionary entries, list
    /// items, cards) this produces correct-looking column geometry. <c>column-fill: balance</c> is solved
    /// per row via a binary search (<see cref="BinarySearchRowTarget"/>) for the minimum column height
    /// that still packs as many children into the row as the full page budget would — tighter than a
    /// single closed-form estimate, especially with unevenly-sized children. True inline-level
    /// fragmentation (splitting a single child's own lines across a column/page boundary) is not
    /// implemented; see docs/html-css-support.md.
    /// </summary>
    internal static class CssLayoutEngineColumns
    {
        public static async ValueTask PerformLayout(RGraphics g, CssBox columnsBox)
        {
            try
            {
                await Layout(g, columnsBox);
            }
            catch (Exception ex)
            {
                columnsBox.HtmlContainer?.ReportError(HtmlRenderErrorType.Layout, "Failed multi-column layout", ex);
            }
        }

        private static async ValueTask Layout(RGraphics g, CssBox columnsBox)
        {
            var htmlContainer = columnsBox.HtmlContainer!;

            // Full width the container spans (all columns + gaps together) — resolved exactly like any
            // other block box's width.
            var containerWidth = await CssLayoutEngine.GetBoxWidth(g, columnsBox);
            columnsBox.ActualRight = columnsBox.Location.X + containerWidth + columnsBox.ActualBoxSizeIncludedWidth;

            var children = columnsBox.Boxes
                .Where(b => b.Display != CssConstants.None && !b.IsOutOfFlow
                            && (b.HtmlTag != null || !b.IsSpaceOrEmpty))
                .ToList();

            if (children.Count == 0)
            {
                columnsBox.ActualBottom = columnsBox.Location.Y + columnsBox.ActualBoxSizeIncludedHeight;
                return;
            }

            // column-gap is shared with flex/grid (same CSS property, see CssBox.FlexColumnGap), whose
            // spec-correct default is 0 — but multicol's own default ("normal") has always rendered as
            // roughly 1em in practice. Since the shared field can't distinguish "explicitly 0" from
            // "never set", treat the shared default value as multicol's 1em default; an explicit
            // `column-gap: 0` is indistinguishable from this and, rarely, will render as ~1em instead.
            var gap = columnsBox.FlexColumnGap == "0"
                ? CssValueParser.ParseLength("1em", containerWidth, columnsBox)
                : CssValueParser.ParseLength(columnsBox.FlexColumnGap, containerWidth, columnsBox);
            var (columnCount, columnWidth) = ResolveColumns(columnsBox, containerWidth, gap);

            if (columnCount <= 1)
            {
                // Degenerates to ordinary single-column block flow — defer to the normal block layout
                // path, which (unlike this engine's atomic-child model) already supports real
                // inline-level page fragmentation via paint-time clipping.
                columnsBox.ActualBottom = columnsBox.Location.Y;
                foreach (var childBox in columnsBox.Boxes)
                {
                    await childBox.PerformLayout(g);
                }

                columnsBox.ActualRight = columnsBox.CalculateActualRight();
                if (columnsBox.Boxes.Any(b => !b.IsOutOfFlow))
                {
                    columnsBox.ActualBottom = columnsBox.MarginBottomCollapse();
                }

                return;
            }

            // Phase 1: lay out every child as one tall, single virtual column at the resolved column
            // width, reusing ordinary block layout untouched. This gives each child its correct natural
            // height (and the natural collapsed-margin gap to the next child) without reimplementing
            // line/box measurement.
            var originalRight = columnsBox.ActualRight;
            columnsBox.ActualRight = columnsBox.Location.X + columnWidth + columnsBox.ActualBoxSizeIncludedWidth;
            columnsBox.ActualBottom = columnsBox.Location.Y;

            // Each child's own PerformLayoutImp unconditionally grows HtmlContainer.ActualSize's
            // monotonic high-water mark using its Phase-1 virtual (un-banded, single-tall-column) bottom,
            // which can be far larger than its real final position. That's harmless when later, real
            // content elsewhere in the document legitimately supersedes it - but for the last multi-column
            // container in a document, nothing supersedes it, permanently inflating the page count with
            // phantom trailing pages. Snapshot/restore around the virtual pass so only Phase 2's real,
            // re-banded geometry (via columnsBox's own ActualBottom below, which flows into ActualSize
            // normally once this method returns) can grow it.
            var actualSizeBeforeVirtualPass = htmlContainer.ActualSize;

            foreach (var childBox in children)
            {
                await childBox.PerformLayout(g);
            }

            htmlContainer.ActualSize = actualSizeBeforeVirtualPass;

            columnsBox.ActualRight = originalRight;

            // Phase 2: re-band each child from its virtual single-column position into its real
            // (page, column) slot, preserving the natural gap the virtual pass already computed between
            // consecutive children that stay in the same column.
            var pageHeight = htmlContainer.PageSize.Height;
            var boxTop = columnsBox.ClientTop;

            var columnLeft = columnsBox.ClientLeft;
            var pitch = columnWidth + gap;

            // Which document-wide page (0-based) the container's own top falls on.
            var startPage = pageHeight > 0 ? htmlContainer.PageIndexOf(boxTop) : 0;

            // Top of the content area of the page `startPage + row` places rows on.
            double PageContentTopOf(int row) => htmlContainer.PageTopOf(startPage + row);

            // Row 0 starts wherever the container itself starts (which may be partway down its page);
            // every later row starts at the top of its page's content area.
            double RowTop(int row) => row == 0 ? boxTop : PageContentTopOf(row);
            // Every row's usable space ends at the top of the *next* row's page, regardless of row 0's
            // partial-page start.
            double RowBottom(int row) => PageContentTopOf(row + 1);

            // column-fill: balance (the spec default, used whenever it isn't explicitly "auto") aims for
            // equal-height columns rather than filling one column all the way before starting the next.
            // Solved per row via BinarySearchRowTarget below: the minimum column height that still packs
            // as many of this row's remaining children into `columnCount` columns as the full page
            // budget would — tighter than a single closed-form estimate, especially with unevenly-sized
            // children, while still preserving the "whole child, never split" model (only ever moves
            // entire children between (page, column) slots, exactly like the non-balanced case).
            var balanceFill = columnsBox.ColumnFill != CssConstants.Auto;

            // effectiveTop is RowTop(r), except when a taller-than-budget forced child (see columnEmpty
            // below) on an earlier row overran its nominal page boundary — then it's that overrun bottom,
            // so this row starts after the earlier overflow instead of visually colliding with it.
            double RowTarget(int r, double effectiveTop, int remainingStartIndex)
            {
                var pageBudget = RowBottom(r) - effectiveTop;
                if (!balanceFill) return pageBudget;

                return BinarySearchRowTarget(children, remainingStartIndex, columnCount, pageBudget);
            }

            var row = 0;
            var col = 0;
            var colTop = RowTop(0);
            var colY = colTop;
            var rowTarget = RowTarget(0, colTop, 0);

            var ruleSegments = new List<(double X, double Top, double Bottom)>();
            var rowMaxBottoms = new Dictionary<int, double>();
            var rowActualTops = new Dictionary<int, double> { [0] = colTop };

            double? previousChildNaturalBottom = null;

            for (var childIndex = 0; childIndex < children.Count; childIndex++)
            {
                var child = children[childIndex];
                var naturalTop = child.Location.Y;
                var naturalBottom = child.ActualBottom;
                var height = naturalBottom - naturalTop;

                var remaining = colTop + rowTarget - colY;
                var columnEmpty = Math.Abs(colY - colTop) < 0.01;

                if (!columnEmpty && height > remaining && pageHeight > 0)
                {
                    // Doesn't fit in the current column — advance to the next column, or (once every
                    // column on this page-row is used) the next page-row's first column.
                    col++;
                    if (col >= columnCount)
                    {
                        col = 0;
                        row++;

                        var nominalTop = RowTop(row);
                        colTop = rowMaxBottoms.TryGetValue(row - 1, out var previousRowOverflow)
                            ? Math.Max(nominalTop, previousRowOverflow)
                            : nominalTop;
                        rowActualTops[row] = colTop;
                        rowTarget = RowTarget(row, colTop, childIndex);
                    }

                    colY = colTop;
                    previousChildNaturalBottom = null;
                }

                // Preserve the virtual pass's natural (collapsed-margin) gap only when staying in the
                // same column right after the previous child; a fragmentation break never carries a
                // leading gap, matching CSS Fragmentation behavior.
                if (previousChildNaturalBottom.HasValue)
                {
                    colY += naturalTop - previousChildNaturalBottom.Value;
                }

                var finalX = columnLeft + col * pitch;
                var finalY = colY;

                var deltaX = finalX - child.Location.X;
                var deltaY = finalY - naturalTop;

                if (Math.Abs(deltaX) > 0.001) child.OffsetLeft(deltaX);
                if (Math.Abs(deltaY) > 0.001) child.OffsetTop(deltaY);

                colY = finalY + height;
                previousChildNaturalBottom = naturalBottom;

                rowMaxBottoms[row] = rowMaxBottoms.TryGetValue(row, out var existing) ? Math.Max(existing, colY) : colY;
            }

            // Column-rule segments: one per gap, spanning the tallest column actually used on each
            // page-row this container occupies.
            foreach (var (r, rowBottom) in rowMaxBottoms)
            {
                var rowTop = rowActualTops[r];

                for (var c = 1; c < columnCount; c++)
                {
                    var ruleX = columnLeft + c * pitch - gap / 2;
                    ruleSegments.Add((ruleX, rowTop, rowBottom));
                }
            }

            columnsBox.ColumnRuleSegments = ruleSegments;
            columnsBox.ActualBottom = rowMaxBottoms.Values.DefaultIfEmpty(boxTop).Max();
        }

        /// <summary>
        /// Finds the minimum column height (between 1 and <paramref name="pageBudget"/>) that still
        /// packs as many of the children starting at <paramref name="startIndex"/> into
        /// <paramref name="columnCount"/> columns as using the full <paramref name="pageBudget"/> would —
        /// i.e. the tightest height that doesn't force this row to hold fewer children than it
        /// otherwise could, which is what <c>column-fill: balance</c> asks for. Assumes packed-child-
        /// count is monotonically non-decreasing in the height budget (a taller budget can only fit the
        /// same children or more, never fewer) — true for this atomic "whole child, never split" model,
        /// including the forced-oversized-child-alone-in-a-column case (that child always claims exactly
        /// one column regardless of budget, so it doesn't break monotonicity).
        /// </summary>
        private static double BinarySearchRowTarget(List<CssBox> children, int startIndex, int columnCount, double pageBudget)
        {
            if (pageBudget <= 1 || startIndex >= children.Count)
                return Math.Max(1, pageBudget);

            var (targetCount, _) = SimulateRowPacking(children, startIndex, columnCount, pageBudget);
            if (targetCount == 0)
                return pageBudget; // nothing fits even at the full budget - let the caller's forced-fit branch handle it

            var lo = 1.0;
            var hi = pageBudget;

            // 30 iterations of bisection on a points-scale budget comfortably exceeds sub-pixel
            // precision long before it matters visually.
            for (var i = 0; i < 30; i++)
            {
                var mid = (lo + hi) / 2;
                var (count, _) = SimulateRowPacking(children, startIndex, columnCount, mid);
                if (count >= targetCount)
                    hi = mid;
                else
                    lo = mid;
            }

            return hi;
        }

        /// <summary>
        /// Read-only dry run of the real packing loop in <see cref="Layout"/>: given a candidate column
        /// height (<paramref name="rowTarget"/>), returns how many of the children starting at
        /// <paramref name="startIndex"/> fit within <paramref name="columnCount"/> columns before the
        /// row would need to overflow into a new one, and the tallest column height that resulted.
        /// Mirrors the real loop's fit-check/columnEmpty/natural-gap logic exactly (relative to the
        /// row's own top, since a candidate height is being evaluated in isolation) so its child count is
        /// a faithful prediction of what the real pass would do at that same target height. Never
        /// mutates any child — only reads each child's <c>Location</c>/<c>ActualBottom</c>, already fixed
        /// by this class's earlier real (single-virtual-column) layout pass.
        /// </summary>
        private static (int PlacedCount, double MaxColumnHeight) SimulateRowPacking(
            List<CssBox> children, int startIndex, int columnCount, double rowTarget)
        {
            var col = 0;
            var colTop = 0.0;
            var colY = colTop;
            var maxColumnHeight = 0.0;
            double? previousChildNaturalBottom = null;

            var i = startIndex;
            for (; i < children.Count; i++)
            {
                var child = children[i];
                var naturalTop = child.Location.Y;
                var naturalBottom = child.ActualBottom;
                var height = naturalBottom - naturalTop;

                var remaining = colTop + rowTarget - colY;
                var columnEmpty = Math.Abs(colY - colTop) < 0.01;

                if (!columnEmpty && height > remaining)
                {
                    col++;
                    if (col >= columnCount)
                        break; // this row is full at this target height - child i belongs to the next row

                    colY = colTop;
                    previousChildNaturalBottom = null;
                }

                if (previousChildNaturalBottom.HasValue)
                    colY += naturalTop - previousChildNaturalBottom.Value;

                colY += height;
                previousChildNaturalBottom = naturalBottom;
                maxColumnHeight = Math.Max(maxColumnHeight, colY - colTop);
            }

            return (i - startIndex, maxColumnHeight);
        }

        /// <summary>
        /// Resolves <c>column-count</c>/<c>column-width</c>/<c>columns</c> to a concrete (count, width)
        /// pair against the container's content-box width, per CSS Multi-column Layout §3-4.
        /// </summary>
        private static (int Count, double Width) ResolveColumns(CssBox columnsBox, double containerWidth, double gap)
        {
            var parsedCount = 0;
            var hasCount = columnsBox.ColumnCount != CssConstants.Auto && int.TryParse(columnsBox.ColumnCount, out parsedCount);
            var hasWidth = columnsBox.ColumnWidth != CssConstants.Auto && CssValueParser.IsValidLength(columnsBox.ColumnWidth);

            var specifiedWidth = hasWidth ? CssValueParser.ParseLength(columnsBox.ColumnWidth, containerWidth, columnsBox) : 0;

            if (hasCount && hasWidth)
            {
                // Both given: column-count is a maximum — never more columns than fit at >= column-width.
                var maxByWidth = specifiedWidth > 0 ? Math.Max(1, (int)((containerWidth + gap) / (specifiedWidth + gap))) : parsedCount;
                var count = Math.Max(1, Math.Min(parsedCount, maxByWidth));
                return (count, Math.Max(0, (containerWidth - gap * (count - 1)) / count));
            }

            if (hasCount)
            {
                var count = Math.Max(1, parsedCount);
                return (count, Math.Max(0, (containerWidth - gap * (count - 1)) / count));
            }

            if (hasWidth && specifiedWidth > 0)
            {
                var count = Math.Max(1, (int)((containerWidth + gap) / (specifiedWidth + gap)));
                return (count, Math.Max(0, (containerWidth - gap * (count - 1)) / count));
            }

            // Neither given (shouldn't normally reach here — EstablishesMultiColumnContext requires one).
            return (1, containerWidth);
        }
    }
}
