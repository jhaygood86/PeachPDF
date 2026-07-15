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
    /// items, cards) this produces correct-looking column geometry. <c>column-fill: balance</c> is
    /// approximated (target an even split of remaining content per row, clamped to the page budget) rather
    /// than solved exactly. True inline-level fragmentation (splitting a single child's own lines across a
    /// column/page boundary) is not implemented; see docs/html-css-support.md.
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

            foreach (var childBox in children)
            {
                await childBox.PerformLayout(g);
            }

            columnsBox.ActualRight = originalRight;

            // Phase 2: re-band each child from its virtual single-column position into its real
            // (page, column) slot, preserving the natural gap the virtual pass already computed between
            // consecutive children that stay in the same column.
            var pageHeight = htmlContainer.PageSize.Height;
            var pageContentTop = htmlContainer.MarginTop;
            var boxTop = columnsBox.ClientTop;

            var columnLeft = columnsBox.ClientLeft;
            var pitch = columnWidth + gap;

            // Which document-wide page (0-based) the container's own top falls on.
            var startPage = pageHeight > 0 ? (int)((boxTop - pageContentTop) / pageHeight) : 0;

            // Top of the content area of the page `startPage + row` places rows on.
            double PageContentTopOf(int row) => pageContentTop + (startPage + row) * pageHeight;

            // Row 0 starts wherever the container itself starts (which may be partway down its page);
            // every later row starts at the top of its page's content area.
            double RowTop(int row) => row == 0 ? boxTop : PageContentTopOf(row);
            // Every row's usable space ends at the top of the *next* row's page, regardless of row 0's
            // partial-page start.
            double RowBottom(int row) => PageContentTopOf(row + 1);

            // column-fill: balance (the spec default, used whenever it isn't explicitly "auto") aims for
            // equal-height columns rather than filling one column all the way before starting the next.
            // True balancing needs an iterative solver; this approximates it well enough for the common
            // cases by targeting "however much of the *remaining* content divides evenly across this
            // row's columns", clamped to the row's actual page budget. When remaining content is short
            // (fits on one page), this naturally spreads it evenly across all columns. When remaining
            // content spans many more page-rows, the clamp makes each row fill to capacity (indistinguishable
            // from sequential fill) until the final, genuinely-balanced row.
            var balanceFill = columnsBox.ColumnFill != CssConstants.Auto;
            var totalNaturalBottom = children[^1].ActualBottom;

            // effectiveTop is RowTop(r), except when a taller-than-budget forced child (see columnEmpty
            // below) on an earlier row overran its nominal page boundary — then it's that overrun bottom,
            // so this row starts after the earlier overflow instead of visually colliding with it.
            double RowTarget(int r, double effectiveTop, double remainingContentTop)
            {
                var pageBudget = RowBottom(r) - effectiveTop;
                if (!balanceFill) return pageBudget;

                var balanced = (totalNaturalBottom - remainingContentTop) / columnCount;
                return Math.Min(pageBudget, Math.Max(1, balanced));
            }

            var row = 0;
            var col = 0;
            var colTop = RowTop(0);
            var colY = colTop;
            var rowTarget = RowTarget(0, colTop, colTop);

            var ruleSegments = new List<(double X, double Top, double Bottom)>();
            var rowMaxBottoms = new Dictionary<int, double>();
            var rowActualTops = new Dictionary<int, double> { [0] = colTop };

            double? previousChildNaturalBottom = null;

            foreach (var child in children)
            {
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
                        rowTarget = RowTarget(row, colTop, naturalTop);
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
