using PeachPDF.Html.Adapters;
using PeachPDF.Html.Core.Entities;
using PeachPDF.Html.Core.Parse;
using PeachPDF.Html.Core.Utils;
using PeachPDF.Html.Core.Fragmentation;
using PeachPDF.Html.Core.Fragments;
using System;
using System.Collections.Frozen;
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
        /// <summary>
        /// The empty answer to both "which boxes continue past this column" and "which boxes is this column
        /// not holding", shared rather than allocated per column: a container whose content fits gives it
        /// for every column, and every container gives it for its last one.
        /// </summary>
        private static readonly IReadOnlySet<CssBox> NoBoxes = FrozenSet<CssBox>.Empty;


        public static async ValueTask PerformLayout(RGraphics g, CssBox columnsBox, BreakToken? resume = null)
        {
            try
            {
                await Layout(g, columnsBox, resume);
            }
            catch (Exception ex)
            {
                if (columnsBox.HtmlContainer is { } container)
                    throw container.RenderError(HtmlRenderErrorType.Layout, "Failed multi-column layout", ex);
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
                var measurementContext = htmlContainer.DetachFragmentainer();

                try
                {
                    foreach (var childBox in children)
                    {
                        await childBox.PerformLayout(g);
                    }
                }
                finally
                {
                    htmlContainer.RestoreFragmentainer(measurementContext);
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
            // A container laid out again from the start may land in a different slot than the one it recorded
            // its columns in, so what it recorded anywhere describes geometry that no longer exists. A
            // resumed one is the opposite case: the columns it filled on earlier pages are still there.
            htmlContainer.ClearNestedFragmentainers(columnsBox, resume is null ? null : startSlot);

            for (var attempt = 0; ; attempt++)
            {
                (carry, contentBottom, filledColumns) =
                    await FillColumns(g, columnsBox, children, resume, boxTop, target, columnLeft, pitch,
                        columnWidth, containerWidth, columnCount, startSlot, htmlContainer);

                if (attempt >= MaxFillAttempts) break;

                // A forced page break ends the flow for this fragment as surely as running out of content
                // does, so this *is* the fragment that balances - but it was filled at a target estimated
                // over content that reaches past the break, which leaves the columns before it unbalanced.
                // The two steps are a continuation's own: fill the budget to learn the real height of what
                // precedes the break, then take an even share of it. Growing the target is never an answer
                // here; nothing about a column height can avoid a page break.
                if (carry is BlockBreakToken { EscapesNestedFragmentainer: true })
                {
                    if (!balances || rebalanced) break;

                    if (target < pageBudget)
                    {
                        target = pageBudget;
                    }
                    else if (contentBottom > boxTop)
                    {
                        rebalanced = true;
                        target = Math.Max(1, (contentBottom - boxTop) / columnCount);
                    }
                    else
                    {
                        break;
                    }
                }
                else if (carry is not null)
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

                // The attempt being discarded recorded columns of its own.
                htmlContainer.ClearNestedFragmentainers(columnsBox, startSlot);
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

                var columnStart = FirstChildIndexOf(carry, children);
                var columnInlineLeft = columnLeft + col * pitch;

                try
                {
                    columnsBox.ActualBottom = boxTop;
                    PlaceColumn(columnsBox, columnInlineLeft, columnWidth);

                    stopped = await columnsBox.FillFragmentainerWithBlockChildren(g, carry);

                    // A child the fill decided to break *before* was laid out here and is about to be
                    // laid out again in the next column, so its geometry is not this column's.
                    var placedBelow = PlacedBelow(columnsBox.PendingBreakToken, children);

                    // And the same is true one level further down, which the index above cannot say:
                    // a break raised inside a nested block leaves that block in this column while
                    // everything from the break onward belongs to the next one.
                    var beyond = BeyondThisColumn(columnsBox.PendingBreakToken);

                    columnBottom = MaxBottomOf(children, placedBelow, beyond);

                    // This column's geometry, handed over while the boxes still hold it. A child
                    // continuing into the next column is laid out again there, at that column's own
                    // inline position, so nothing read afterwards could tell the two fragments apart -
                    // a box carries one Location, and both halves sit in the same page band (#366).
                    //
                    // The band is the one this column *filled*, which unbreakable content can make taller
                    // than the target it was given (css-multicol-1 §3.3: a column is at least as tall as
                    // its unbreakable content). A child this engine cannot fragment - a table, another
                    // engine's container - is placed whole and overflows a target derived from balancing,
                    // and recording the target would leave everything of it below that target claimed by
                    // no fragmentainer at all: laid out, and painted nowhere. Extending the block axis
                    // cannot claim a neighbouring column's content, because columns are told apart by the
                    // inline axis, and nothing outside this container is ever asked about this region.
                    htmlContainer.RecordNestedFragmentainer(
                        columnsBox,
                        startSlot,
                        (boxTop, Math.Max(boxTop + target, columnBottom)),
                        (columnInlineLeft, columnInlineLeft + columnWidth),
                        BoxGeometrySnapshot.Capture(ChildrenIn(children, columnStart, placedBelow), beyond),
                        ContinuingPast(columnsBox.PendingBreakToken));
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

                // A forced page break is not this container's to satisfy: every column it could open is
                // on the page the break is leaving. The record says so, so the column loop ends here and
                // the remainder travels up to the page driver with the target and slot it already names.
                if (columnsBox.PendingBreakToken is BlockBreakToken { EscapesNestedFragmentainer: true })
                {
                    carry = columnsBox.TakePendingBreakToken();
                    break;
                }

                carry = ResumeInTheNextColumn(columnsBox, boxTop, startSlot);
            }

            return (carry, contentBottom, filledColumns);
        }

        /// <summary>
        /// The boxes <paramref name="token"/> says continue past this column — the chain from the container
        /// down to the box that stopped, minus the container itself, which is not a fragment of this column.
        /// </summary>
        /// <remarks>
        /// Read off the break record rather than inferred from geometry, because that is what the record is:
        /// css-break-3's own statement of which boxes carry on into the next fragmentainer. A box in it has
        /// not run its epilogue, so its height is unresolved and the emitter has to take its fragment's
        /// extent from the content it placed here instead.
        /// </remarks>
        private static IReadOnlySet<CssBox> ContinuingPast(BreakToken? token)
        {
            if (token is not BlockBreakToken { ChildToken: not null } block) return NoBoxes;

            var continuing = new HashSet<CssBox>();

            for (var link = block.ChildToken; link is not null;)
            {
                continuing.Add(link.Box);
                link = link is BlockBreakToken { ChildToken: { } child } ? child : null;
            }

            return continuing;
        }

        /// <summary>
        /// The children one column holds: from the one its fill began at, up to (but not including) the one
        /// the break falls before.
        /// </summary>
        /// <remarks>
        /// The lower bound matters as much as the upper one. Children below it belong to earlier columns and
        /// still hold that column's geometry; the one <i>at</i> it is included, because a child resuming here
        /// is genuinely in both this column and the previous one — which is the whole case per-fragment
        /// geometry exists for.
        /// </remarks>
        private static IEnumerable<CssBox> ChildrenIn(List<CssBox> children, int from, int through)
        {
            for (var i = Math.Max(0, from); i < through && i < children.Count; i++)
            {
                yield return children[i];
            }
        }

        /// <summary>
        /// Undoes the fill attempt just made, so the same children can be filled again at a new target.
        /// </summary>
        /// <remarks>
        /// Goes through the shared rollback (<see cref="PassRewind.RollBackTo"/>), which the driver's own
        /// pass re-entry needs in exactly the same terms — including its no-record branch, which is what
        /// the pass that starts this container takes.
        /// </remarks>
        private static void ResetChildrenForRefill(List<CssBox> children, BreakToken? resume) =>
            PassRewind.RollBackTo(resume, children);

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

        /// <summary>
        /// The boxes this column does <b>not</b> hold, read off the record it stopped at — at every level
        /// of the chain rather than only the container's own.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <see cref="PlacedBelow"/> answers this for the container's own children, which is the whole
        /// answer while a child is atomic per column. It is not the whole answer once a break can be raised
        /// <i>inside</i> a child: the child stays, and what belongs to the next column is the part of it
        /// from the break onward — the siblings at and after the boundary at each nested level.
        /// </para>
        /// <para>
        /// A break <i>before</i> a box means the box itself is beyond this column; a break <i>inside</i>
        /// one means it is here, up to where it stopped, and the chain descends into it. Everything after
        /// the boundary is beyond either way, and the fill never reached it — so it still carries the
        /// <i>measurement</i> pass's geometry, one tall virtual column down the document. Measuring it
        /// sized this column to the whole flow; capturing it described content in a column it is not in,
        /// and left the same word claimed by two fragments at once.
        /// </para>
        /// <para>
        /// The walk stops at an inline record. A box whose own flow stopped mid-line is in this column, and
        /// which of its words are not is already said per word
        /// (<see cref="CssRect.AwaitsTheNextFragmentainer"/>).
        /// </para>
        /// </remarks>
        private static IReadOnlySet<CssBox> BeyondThisColumn(BreakToken? token)
        {
            // The last column of every container reaches here with no record at all, and so does every
            // column of one whose content fits - so the empty answer is the common one, and it needs no
            // set of its own.
            if (token is not BlockBreakToken outermost) return NoBoxes;

            var beyond = new HashSet<CssBox>();

            for (var link = outermost; link is not null; link = link.ChildToken as BlockBreakToken)
            {
                var from = link.IsBreakBefore ? link.ResumeChildIndex : link.ResumeChildIndex + 1;

                for (var i = from; i < link.Box.Boxes.Count; i++)
                {
                    beyond.Add(link.Box.Boxes[i]);
                }
            }

            return beyond;
        }

        private static double MaxBottomOf(List<CssBox> children, int limit, IReadOnlySet<CssBox> beyond)
        {
            var bottom = double.MinValue;

            for (var i = 0; i < children.Count && i < limit; i++)
            {
                bottom = Math.Max(bottom, DeepestBottomOf(children[i], beyond));
            }

            return bottom is double.MinValue ? 0 : bottom;
        }

        /// <summary>
        /// How far down a subtree's geometry actually reaches.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Deeper than <see cref="CssBox.ActualBottom"/> for a box that continues into the next
        /// column: its height is resolved by the epilogue, which runs only on the pass that <i>completes</i>
        /// it, so mid-flow it still reports the zero-height box it was placed as. Reading that alone sizes the
        /// container to less than the column really holds, and everything after it overlaps the overflow.
        /// </para>
        /// <para>
        /// <b>The line boxes the pass kept are the honest source, and its words are not.</b> A flow that stops
        /// re-places only the words up to the break; every word after it still carries the position the
        /// measurement pass gave it, one tall virtual column down the document — so measuring words sized the
        /// container to the whole flow's height and left a column's worth of blank space below the content.
        /// <c>CreateLineBoxes</c> discards the line it stopped in, so what remains in
        /// <see cref="CssBox.LineBoxes"/> is exactly what this fragmentainer holds.
        /// </para>
        /// <para>
        /// Only a box that positions itself owns its <see cref="CssBox.ActualBottom"/>: an inline or
        /// bare text box holds either its previous sibling's coordinates or a line-local value layout never
        /// finished with, so asking one how far down it reaches names the wrong box.
        /// </para>
        /// </remarks>
        private static double DeepestBottomOf(CssBox box, IReadOnlySet<CssBox> beyond)
        {
            var bottom = box.PlacesItselfAsBlockBox ? box.ActualBottom : double.MinValue;

            foreach (var line in box.LineBoxes)
            {
                bottom = Math.Max(bottom, line.LineBottom);
            }

            foreach (var childBox in box.Boxes)
            {
                if (beyond.Contains(childBox)) continue;

                bottom = Math.Max(bottom, DeepestBottomOf(childBox, beyond));
            }

            return bottom;
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
            return PrecedingRealChildren(block, boundary, children);
        }

        private static int PrecedingRealChildren(BlockBreakToken token, CssBox boundary, List<CssBox> children)
        {
            var precedingIndex = token.Box.Boxes.IndexOf(boundary);

            return children.Count(c => token.Box.Boxes.IndexOf(c) < precedingIndex);
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
        /// <b>And a break <i>inside</i> a child stands, so a child genuinely continues into the next
        /// column.</b> It used to be rewritten as a break before that child, because a box carries a single
        /// <c>Location</c> and columns sit side by side inside one page band — so a box split across two of
        /// them had both halves at the same document Y <i>and</i> the same X, with its continuation lines
        /// laid out over the ones already there and its background drawable in one column only. What makes
        /// it work now is that a fragment carries geometry of its own
        /// (<see href="https://github.com/jhaygood86/PeachPDF/issues/366">#366</see>): each column hands the
        /// emitter the geometry its content had while it was being filled, and the resumed box is re-placed
        /// in the inline axis rather than keeping the position of the column it has left.
        /// </para>
        /// <para>
        /// The slot is restated as this container's own. A break decided inside a column was worked out
        /// against the page grid, whose next slot is the next <i>page</i> — the next column is on this one.
        /// </para>
        /// <para>
        /// <b>Every link of the chain is restated, not only the outermost.</b> A break raised below a
        /// top-level child produces a chain, and each link of it was decided against the page grid just as
        /// the outermost one was — so a deeper link left alone carries a slot several pages on and a target
        /// somewhere down the document, which the next column then places its content at. Measured as a
        /// resumed page whose second column began 400pt past the page it is on.
        /// </para>
        /// </remarks>
        private static BreakToken? ResumeInTheNextColumn(CssBox columnsBox, double bandTop, int startSlot)
        {
            if (columnsBox.TakePendingBreakToken() is not { } token) return null;

            return RestatedInTheNextColumn(token, bandTop, startSlot, outermost: true);
        }

        /// <summary>
        /// One link of a resumption record, restated in the next column's terms.
        /// </summary>
        /// <remarks>
        /// Only the <b>outermost</b> link's target is this container's to state: its box is a top-level
        /// child, so it begins the next column and the band top is where it goes. A deeper link's box
        /// begins its own containing block's content instead, and where <i>that</i> lands in the next
        /// column is not known until it has been re-placed there — so the target is dropped rather than
        /// carried, and re-derived at placement time
        /// (<c>CssBox.ColumnTopForTheChildThisFillBeginsAt</c>).
        /// <para>
        /// <b>The non-block arm is reached only by the recursion, never by the outermost call.</b> A record
        /// this container hands over is built by its own child loop, which always wraps what it stopped at
        /// in a <see cref="BlockBreakToken"/> — but the innermost link of that chain is an
        /// <see cref="InlineBreakToken"/> whenever the box that stopped is a block whose content is text,
        /// which is the ordinary "a paragraph continues into the next column" case. Verified by throwing
        /// here: with the throw restricted to <paramref name="outermost"/> the whole suite passes, and
        /// without that restriction 29 tests fail. So this arm carries real documents and must restate the
        /// slot rather than reject the record; a line index needs no target, since the block holding those
        /// lines is placed by the link above it.
        /// </para>
        /// </remarks>
        private static BreakToken RestatedInTheNextColumn(
            BreakToken token, double bandTop, int startSlot, bool outermost)
        {
            if (token is not BlockBreakToken block) return token with { ResumeSlotIndex = startSlot };

            return block with
            {
                ResumeSlotIndex = startSlot,
                ResumeTopOverride = block.IsBreakBefore && outermost ? bandTop : null,
                ChildToken = block.ChildToken is null
                    ? null
                    : RestatedInTheNextColumn(block.ChildToken, bandTop, startSlot, outermost: false)
            };
        }

        /// <summary>
        /// The child a column's fill begins at, as an index into <paramref name="children"/>.
        /// </summary>
        /// <remarks>
        /// A token's own <c>ResumeChildIndex</c> is an index into the container's whole <c>Boxes</c> list,
        /// which also holds the out-of-flow and <c>display: none</c> boxes this engine filtered out — so it
        /// cannot be used against the filtered list directly, which is the same mapping
        /// <see cref="PlacedBelow"/> performs at the other end of the range.
        /// </remarks>
        private static int FirstChildIndexOf(BreakToken? carry, List<CssBox> children)
        {
            if (carry is not BlockBreakToken block) return 0;

            var boundary = block.Box.Boxes[block.ResumeChildIndex];
            var index = children.IndexOf(boundary);

            return index >= 0 ? index : PrecedingRealChildren(block, boundary, children);
        }

        /// <summary>
        /// Restates a column-relative resumption record in page terms, for the content the last column
        /// could not hold.
        /// </summary>
        /// <remarks>
        /// A record that <see cref="BlockBreakToken.EscapesNestedFragmentainer">escapes</see> this
        /// container is already in page terms and is left exactly as it is — that is the whole of what the
        /// mark buys. Its target may be several slots on (a directional break reserves a blank page to
        /// land on the requested side), which "the slot after this one" would quietly discard.
        /// </remarks>
        private static BreakToken? RetargetToTheNextPage(
            BreakToken carry, HtmlContainerInt container, int startSlot)
        {
            if (carry is not BlockBreakToken block) return carry;

            if (block.EscapesNestedFragmentainer) return block;

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

            // An even share of the content is the floor, and it is what makes balancing mean anything for
            // content the fill can divide. The whole-child packing below answers "how tall must a column be
            // for this many *whole* children to fit", which for a container holding one long child is
            // vacuous - a child alone in a column always fits, so the search converges on nothing at all and
            // the container renders as one tall column. The two combine rather than compete: unsplittable
            // children can force a column taller than the even share, never shorter.
            var evenShare = (children[^1].ActualBottom - children[startIndex].Location.Y) / columnCount;

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

            return Math.Min(pageBudget, Math.Max(hi, evenShare));
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

                // The gap this child inherits from the one above it - its own margin, or whatever
                // separated them naturally - counts towards whether it fits, exactly as it does in the real
                // fill. Asking about the height alone lets a child exceed the target by that gap, so the
                // estimate under-shoots what the fill really needs by one gap per column. While a child was
                // atomic per column that was invisible: the real fill simply placed one child fewer, the
                // growth loop below bumped the target by 20%, and the slack it left was unusable. Now that
                // content genuinely splits, a line moves into that slack and the columns read as unbalanced.
                var gapAbove = previousChildNaturalBottom.HasValue
                    ? naturalTop - previousChildNaturalBottom.Value
                    : 0;

                var remaining = colTop + rowTarget - colY;
                var columnEmpty = Math.Abs(colY - colTop) < 0.01;

                if (!columnEmpty && gapAbove + height > remaining)
                {
                    col++;
                    if (col >= columnCount)
                        break; // this row is full at this target height - child i belongs to the next row

                    colY = colTop;
                    gapAbove = 0;
                    previousChildNaturalBottom = null;
                }

                colY += gapAbove + height;
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
