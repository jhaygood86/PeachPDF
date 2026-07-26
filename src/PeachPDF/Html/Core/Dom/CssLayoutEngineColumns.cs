using PeachPDF.Html.Adapters;
using PeachPDF.Html.Core.Entities;
using PeachPDF.Html.Core.Parse;
using PeachPDF.Html.Core.Utils;
using PeachPDF.Html.Core.Fragmentation;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PeachPDF.Html.Core.Dom
{
    /// <summary>
    /// Lays out a CSS Multi-column Layout container (<c>column-count</c>/<c>column-width</c>/<c>columns</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// A column is a fragmentainer (<see href="https://www.w3.org/TR/css-break-3/#fragmentainer">§2</see>:
    /// "a column in multi-column layout, or a page in paged media"), so this engine is a <b>driver over
    /// its own fragmentainers</b>, in the same shape as <c>HtmlContainerInt.LayoutDocument</c>: it
    /// establishes a fragmentation context per column, fills it through the ordinary block-children loop
    /// (<see cref="CssBox.FillFragmentainerWithBlockChildren"/>), reads back a break token, and opens the
    /// next column at that point. Content therefore splits inside a child — a paragraph continues into
    /// the next column rather than moving to it whole.
    /// </para>
    /// <para>
    /// The <b>measurement pass</b> is kept, and is the reason this is not simply a loop.
    /// <c>column-fill: balance</c> has to know how tall the content is before it can choose a column
    /// height, and the only way to know that is to lay it out; bisecting over real per-column driver runs
    /// is not an acceptable cost. So every child is still laid out once as a single tall virtual column,
    /// with breaking suppressed, purely to size the fill — and then laid out again for real.
    /// </para>
    /// <para>
    /// Every column shares one band, <c>[containerTop, containerTop + target)</c>: columns differ in the
    /// inline axis, not the block axis. That is what lets the block-axis machinery be reused untouched —
    /// a resumed column starts at the same <c>ResumeContentTop</c> a resumed page would.
    /// </para>
    /// </remarks>
    internal static class CssLayoutEngineColumns
    {
        public static async ValueTask PerformLayout(RGraphics g, CssBox columnsBox, BreakToken? resume = null)
        {
            try
            {
                await Layout(g, columnsBox, resume);
            }
            catch (Exception ex)
            {
                columnsBox.HtmlContainer?.ReportError(HtmlRenderErrorType.Layout, "Failed multi-column layout", ex);
            }
        }

        private static async ValueTask Layout(RGraphics g, CssBox columnsBox, BreakToken? resume)
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

            // Phase 1 (measurement): lay out every child as one tall, single virtual column at the
            // resolved column width, reusing ordinary block layout untouched. Breaking stays suppressed
            // here - these are provisional positions spanning many page bands, and a token recorded
            // against them would name a place nothing ends up. Its only product is how tall the content
            // is, which is what column-fill: balance needs before it can pick a column height.
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
            // Only on the pass that starts this container. A resumed one continues children the earlier
            // fragment already measured, and re-measuring them would overwrite the geometry it placed.
            if (resume is null)
            {
                var actualSizeBeforeVirtualPass = htmlContainer.ActualSize;
                var measurementContext = htmlContainer.CurrentFragmentainer;
                var wasFragmenting = measurementContext?.EnterMonolithic() ?? false;

                try
                {
                    foreach (var childBox in children)
                    {
                        await childBox.PerformLayout(g);
                    }
                }
                finally
                {
                    measurementContext?.ExitMonolithic(wasFragmenting);
                }

                htmlContainer.ActualSize = actualSizeBeforeVirtualPass;
            }

            columnsBox.ActualRight = originalRight;

            // Phase 2: fill each column as a real fragmentainer, in the same shape LayoutDocument
            // fills pages - establish a context, run the ordinary block-children loop into it, read back
            // where it stopped, open the next one there.
            // A continuation starts at the fragmentainer it resumed into, not at the container's own top,
            // which is back on the page this one is continuing from.
            var boxTop = resume is not null && htmlContainer.CurrentFragmentainer is { } resumed
                ? resumed.ResumeContentTop
                : columnsBox.ClientTop;

            var columnLeft = columnsBox.ClientLeft;
            var pitch = columnWidth + gap;

            var startSlot = htmlContainer.HasRealPageGrid ? htmlContainer.PageIndexOf(boxTop) : 0;

            // What is left of this container's own page. A column can never be taller than that, so it
            // is the ceiling on every target below.
            var pageBudget = htmlContainer.HasRealPageGrid
                ? htmlContainer.PageBottomOf(startSlot) - boxTop
                : double.MaxValue / 4;

            // column-fill: balance (the default) aims for equal-height columns rather than filling each
            // one before starting the next. An even share of the measured height is the ideal, but it is
            // only reachable where the content can actually be divided that finely: a child with no
            // internal break point of its own - an explicit height, a replaced element - claims its whole
            // depth wherever it lands. So the target is searched for rather than computed, against a
            // whole-child packing of the measurement pass. That estimate is deliberately pessimistic now
            // that content genuinely splits: anything the real fill can divide only packs tighter than
            // the estimate assumed, never looser, so the target is never too small.
            // Balancing applies to the fragment that holds the *end* of the flow; one that overflows into
            // another fills its columns instead. Which of the two this is cannot be known before filling
            // it, so the first fragment starts from the estimate (its measurement pass has just said how
            // tall everything is) and a continuation starts from the full budget and is re-balanced below
            // once the fill has shown that the remainder ends here.
            var balances = columnsBox.ColumnFill != CssConstants.Auto;

            var target = balances && resume is null
                ? EstimateBalancedColumnHeight(children, 0, columnCount, pageBudget)
                : pageBudget;

            var ruleSegments = new List<(double X, double Top, double Bottom)>();

            // The measurement pass ran every child's prologue, which is once-per-box and owns
            // RectanglesReset. The real fill lays the same boxes out again from scratch, so it has to be
            // let back in - the same thing the keep-with-next retry does before re-entering a box.
            ResetChildrenForRefill(children, resume);

            BreakToken? carry;
            double contentBottom;
            int filledColumns;
            var rebalanced = false;

            // The estimate above packs whole children, so it can land a little under what the real fill
            // needs and spill the tail onto the next page - which reads as a column count that changed
            // mid-document rather than as balancing. Where that happens the target is grown and the fill
            // run again, up to the page's own budget, which is the point at which balancing has given up
            // and the content genuinely does not fit this fragment.
            for (var attempt = 0; ; attempt++)
            {
                (carry, contentBottom, filledColumns) =
                    await FillColumns(g, columnsBox, children, resume, boxTop, target, columnLeft, pitch,
                        columnWidth, containerWidth, columnCount, startSlot, htmlContainer);

                if (attempt >= MaxFillAttempts) break;

                if (carry is not null)
                {
                    if (target >= pageBudget) break;

                    target = Math.Min(pageBudget, target * TargetGrowthPerAttempt + 1);
                }
                // Only a fill that used the whole budget - it was not balanced, so this is the fragment
                // that holds the end of the flow and now knows its real height. A fill made at the
                // estimate is already balanced, and re-deriving a target from its own result would shrink
                // it every time and eventually spill the tail.
                else if (balances && !rebalanced && target >= pageBudget && contentBottom > boxTop)
                {
                    // The remainder ended here, so this is the fragment that balances - but it was filled
                    // at the full budget and poured everything into the first column. Now that its real
                    // height is known, an even share of it is the target.
                    rebalanced = true;
                    target = Math.Max(1, (contentBottom - boxTop) / columnCount);
                }
                else
                {
                    break;
                }

                ResetChildrenForRefill(children, resume);
            }

            // One rule per gap between the columns actually used, spanning the content they hold.
            for (var c = 1; c < filledColumns; c++)
            {
                ruleSegments.Add((columnLeft + c * pitch - gap / 2, boxTop, contentBottom));
            }

            // Accumulated, not assigned: this container is laid out once per page fragment, and the last
            // fragment's rules are not the only ones drawn.
            columnsBox.ColumnRuleSegments = resume is null || columnsBox.ColumnRuleSegments is null
                ? ruleSegments
                : [.. columnsBox.ColumnRuleSegments, .. ruleSegments];

            columnsBox.ActualBottom = Math.Max(boxTop, contentBottom);

            // The column loop narrowed the container to one column at a time, and the shared child loop
            // walks every box - so an out-of-flow child resolved its position against a *column*. The
            // containing block css-multicol gives it is the multi-column container, so they are laid out
            // once more here, with the container back at its own width.
            await columnsBox.LayoutOutOfFlowChildrenAgain(g);

            // Content the last column could not hold belongs to the next page, and this container is not
            // the fragmentation context root - so the token travels up the ordinary chain and the page
            // driver opens it, exactly as it would for any other block box that did not finish.
            if (carry is not null)
            {
                columnsBox.SetPendingBreakToken(RetargetToTheNextPage(carry, htmlContainer, startSlot));
            }
        }

        private const int MaxFillAttempts = 4;
        private const double TargetGrowthPerAttempt = 1.2;

        /// <summary>
        /// Fills this container's columns once at <paramref name="target"/>, returning what would not fit,
        /// how far down the content reached, and how many columns it took.
        /// </summary>
        private static async ValueTask<(BreakToken? Carry, double ContentBottom, int FilledColumns)> FillColumns(
            RGraphics g, CssBox columnsBox, List<CssBox> children, BreakToken? resume,
            double boxTop, double target, double columnLeft, double pitch, double columnWidth,
            double containerWidth, int columnCount, int startSlot, HtmlContainerInt htmlContainer)
        {
            var carry = resume;
            var contentBottom = boxTop;
            var filledColumns = 0;

            for (var col = 0; col < columnCount; col++)
            {
                // Every column shares one band: columns differ in the inline axis, not the block axis.
                // A column's own inline span is applied by placing it, below.
                var column = new FragmentainerContext(
                    htmlContainer, columnsBox, startSlot, (boxTop, boxTop + target),
                    inheritsSuppression: true);

                var previousContext = htmlContainer.EnterNestedFragmentainer(column);

                bool stopped;
                var columnBottom = boxTop;

                var columnStart = FirstChildIndexOf(carry);

                try
                {
                    columnsBox.ActualBottom = boxTop;
                    PlaceColumn(columnsBox, columnLeft + col * pitch, columnWidth);

                    stopped = await columnsBox.FillFragmentainerWithBlockChildren(g, carry);

                    // A child the fill decided to break *before* was laid out here and is about to be
                    // laid out again in the next column, so its geometry is not this column's.
                    columnBottom = MaxBottomOf(children, PlacedBelow(columnsBox.PendingBreakToken, children));
                }
                finally
                {
                    htmlContainer.LeaveNestedFragmentainer(previousContext);
                    PlaceColumn(columnsBox, columnLeft, containerWidth);
                }

                filledColumns = col + 1;
                contentBottom = Math.Max(contentBottom, columnBottom);

                if (!stopped)
                {
                    carry = null;
                    break;
                }

                carry = ResumeInTheNextColumn(columnsBox, boxTop, columnStart);
            }

            return (carry, contentBottom, filledColumns);
        }

        /// <summary>Lets every child's prologue run again, for a fill that is being attempted afresh.</summary>
        private static void ResetChildrenForRefill(List<CssBox> children, BreakToken? resume)
        {
            if (resume is not null) return;

            foreach (var child in children)
            {
                child.ResetForRefill();
            }
        }

        /// <summary>
        /// Narrows the container's own inline extent to one column, so children lay out at that column's
        /// X rather than being translated there afterwards.
        /// </summary>
        /// <remarks>
        /// A translation cannot work once content genuinely splits: a box that continues from one column
        /// into the next is a single <c>CssBox</c> whose earlier lines are already placed, and
        /// <c>OffsetLeft</c> would move those too. Laying each column out at its own X is what keeps a
        /// continuation's lines where they belong.
        /// </remarks>
        private static void PlaceColumn(CssBox columnsBox, double left, double width)
        {
            columnsBox.Location = columnsBox.Location with { X = left - columnsBox.ActualPaddingLeft - columnsBox.ActualBorderLeftWidth };
            columnsBox.ActualRight = columnsBox.Location.X + width + columnsBox.ActualBoxSizeIncludedWidth;
        }

        private static double MaxBottomOf(List<CssBox> children, int limit)
        {
            var bottom = double.MinValue;

            for (var i = 0; i < children.Count && i < limit; i++)
            {
                bottom = Math.Max(bottom, children[i].ActualBottom);
            }

            return bottom is double.MinValue ? 0 : bottom;
        }

        /// <summary>
        /// How many of <paramref name="children"/> this column really holds.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Load-bearing, and not merely a tidy-up. Children the fill never reached still carry the
        /// geometry the <i>measurement</i> pass gave them — one tall virtual column — so asking all of
        /// them how far down they go reports the height of the whole flow rather than of this column.
        /// That inflates the container to a full page, which then makes it escape to the next one, which
        /// is how a document that fits in two pages became six.
        /// </para>
        /// <para>
        /// A break <i>before</i> a child means that child is not here; a break <i>inside</i> one means it
        /// is, up to the point it stopped. The token's index is into the container's own <c>Boxes</c>,
        /// which also holds the out-of-flow and <c>display: none</c> boxes this engine filtered out, so it
        /// is mapped back through the filtered list rather than used directly.
        /// </para>
        /// </remarks>
        private static int PlacedBelow(BreakToken? token, List<CssBox> children)
        {
            if (token is not BlockBreakToken block) return children.Count;

            var boundary = block.Box.Boxes[block.ResumeChildIndex];
            var index = children.IndexOf(boundary);

            if (index >= 0) return block.IsBreakBefore ? index : index + 1;

            // The boxes this engine filtered out - out-of-flow, display:none - are exactly the ones that
            // cannot be found here. Counting them as "everything" would measure children the fill never
            // reached, which still carry the measurement pass's tall-single-column geometry: the very
            // inflation this method exists to avoid. How many real children precede the boundary is the
            // answer either way.
            var precedingIndex = block.Box.Boxes.IndexOf(boundary);

            return children.Count(c => block.Box.Boxes.IndexOf(c) < precedingIndex);
        }

        /// <summary>
        /// The record the next column resumes from: the one this column produced, restated in the next
        /// column's terms.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Two restatements. A break-before carries the target the deciding site worked out, and inside a
        /// column that site worked it out against the <i>page</i> grid — the next page's top, which is not
        /// where the next column starts. Every column begins at the same place, so that is what it is
        /// replaced with.
        /// </para>
        /// <para>
        /// <b>And a break <i>inside</i> a child becomes a break before it, so no child ever occupies two
        /// columns at once.</b> This is a real limit rather than a simplification, and it is the same one
        /// the whole-child model had: a box carries a single <c>Location</c>, which can describe its
        /// position in one column only. Columns sit side by side inside one page band, so a box split
        /// across two of them has both halves at the same document Y and one X — its continuation lines
        /// are laid out over the ones already there, and the fragment builder, whose band membership is a
        /// question about Y alone, cannot tell the halves apart to draw its background in both. Splitting
        /// a child across columns needs geometry held per fragment rather than per box, which is
        /// <see href="https://github.com/jhaygood86/PeachPDF/issues/331">#331</see>.
        /// </para>
        /// <para>
        /// A child that begins this column and still does not fit is left alone: it has nowhere better to
        /// be, and breaking before it would ask the same question of every column in turn.
        /// </para>
        /// </remarks>
        private static BreakToken? ResumeInTheNextColumn(CssBox columnsBox, double bandTop, int columnStart)
        {
            if (columnsBox.TakePendingBreakToken() is not BlockBreakToken token) return null;

            if (token.IsBreakBefore) return token with { ResumeTopOverride = bandTop };

            if (token.ResumeChildIndex <= columnStart) return token;

            return new BlockBreakToken(
                token.Box, token.ResumeSlotIndex, token.ResumeChildIndex, null,
                IsBreakBefore: true, bandTop);
        }

        /// <summary>The child index a column's fill begins at.</summary>
        private static int FirstChildIndexOf(BreakToken? carry) =>
            carry is BlockBreakToken block ? block.ResumeChildIndex : 0;

        /// <summary>
        /// Restates a column-relative resumption record in page terms, for the content the last column
        /// could not hold.
        /// </summary>
        private static BreakToken? RetargetToTheNextPage(
            BreakToken carry, HtmlContainerInt container, int startSlot)
        {
            if (carry is not BlockBreakToken block) return carry;

            var nextSlot = startSlot + 1;

            return block with
            {
                ResumeSlotIndex = nextSlot,
                ResumeTopOverride = block.IsBreakBefore && container.HasRealPageGrid
                    ? container.PageTopOf(nextSlot)
                    : block.ResumeTopOverride
            };
        }

        /// <summary>
        /// Estimates a column height for <c>column-fill: balance</c>: the minimum (between 1 and
        /// <paramref name="pageBudget"/>) that still
        /// packs as many of the children starting at <paramref name="startIndex"/> into
        /// <paramref name="columnCount"/> columns as using the full <paramref name="pageBudget"/> would —
        /// i.e. the tightest height that doesn't force this row to hold fewer children than it
        /// otherwise could, which is what <c>column-fill: balance</c> asks for. Assumes packed-child-
        /// count is monotonically non-decreasing in the height budget (a taller budget can only fit the
        /// same children or more, never fewer) — true for this atomic "whole child, never split" model,
        /// including the forced-oversized-child-alone-in-a-column case (that child always claims exactly
        /// one column regardless of budget, so it doesn't break monotonicity).
        /// </summary>
        private static double EstimateBalancedColumnHeight(List<CssBox> children, int startIndex, int columnCount, double pageBudget)
        {
            if (pageBudget <= 1 || startIndex >= children.Count)
                return Math.Max(1, pageBudget);

            var (targetCount, _) = SimulateWholeChildPacking(children, startIndex, columnCount, pageBudget);
            if (targetCount == 0)
                return pageBudget; // nothing fits even at the full budget - let the caller's forced-fit branch handle it

            var lo = 1.0;
            var hi = pageBudget;

            // 30 iterations of bisection on a points-scale budget comfortably exceeds sub-pixel
            // precision long before it matters visually.
            for (var i = 0; i < 30; i++)
            {
                var mid = (lo + hi) / 2;
                var (count, _) = SimulateWholeChildPacking(children, startIndex, columnCount, mid);
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
        private static (int PlacedCount, double MaxColumnHeight) SimulateWholeChildPacking(
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
