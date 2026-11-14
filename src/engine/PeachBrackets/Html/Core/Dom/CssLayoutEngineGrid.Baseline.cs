using PeachBrackets.Css;
using PeachBrackets.Html.Utils;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PeachBrackets.Html.Dom
{
    /// <summary>Baseline alignment of grid items in the row axis (css-align-3 section 9, css-grid-1 section 6.6).</summary>
    internal sealed partial class CssLayoutEngineGrid
    {
        /// <summary>How far a baseline sits from the two edges of an item's margin box along the axis it aligns in.</summary>
        private readonly record struct BaselineSpan(double Above, double Below);

        /// <summary>The baseline alignment <paramref name="alignValue"/> asks for: null when it is no baseline alignment, otherwise whether it is the
        /// <c>last baseline</c> one.</summary>
        private static bool? BaselineKind(string alignValue) =>
            IsBaselineAlign(alignValue) ? alignValue.Contains("last", StringComparison.OrdinalIgnoreCase) : null;

        /// <summary>
        /// The first (or last) baseline of a laid-out item in the row axis, as its distances to the margin box's start and end edges. An item with
        /// no line of text in that axis - an empty box, or one whose writing mode is orthogonal to the grid's, whose row axis is its inline axis -
        /// has its baseline synthesized from its border box (css-align-3 section 9.1): the line-under edge, so its bottom margin lies below it.
        /// </summary>
        private BaselineSpan ItemRowBaseline(CssBox box, bool last)
        {
            var marginTop = box.ActualMarginTop;
            var height = box.ActualBoxSizingHeight + marginTop + box.ActualMarginBottom;
            var offset = box.WritingMode.Value.IsVertical() == _vertical ? FirstOrLastBaselineOffset(box, last) : null;
            var above = offset is { } line ? marginTop + line : height - box.ActualMarginBottom;
            return new BaselineSpan(above, height - above);
        }

        /// <summary>The baseline of the first (or last) line of text in the subtree, from the box's border-box top.</summary>
        private static double? FirstOrLastBaselineOffset(CssBox box, bool last)
        {
            if (!last) return BaselineAlignment.GetItemBaselineOffset(box);

            var line = FindLastLineBox(box);
            if (line is null || line.Words.Count == 0) return null;

            var word = line.Words[^1];
            return word.Top - box.Location.Y + word.OwnerBox.ActualFont.Ascent;
        }

        private static CssLineBox? FindLastLineBox(CssBox box)
        {
            for (var i = box.Boxes.Count - 1; i >= 0; i--)
            {
                var child = box.Boxes[i];
                if (child.DerivedStyle.ActualDisplay == Keywords.None || child.IsOutOfFlow) continue;
                if (FindLastLineBox(child) is { } found) return found;
            }

            return box.LineBoxes.Count > 0 ? box.LineBoxes[^1] : null;
        }

        /// <summary>
        /// Aligns the items of each row whose used <c>align-items</c>/<c>align-self</c> is <c>baseline</c> (or <c>last baseline</c>), which were
        /// placed at the start (or end) of their cell: a group shares one baseline, so each item moves by how far its own baseline is short of the
        /// group's furthest one from that edge. The row was already sized to hold each group (<see cref="BaselineGroupSize"/>).
        /// </summary>
        private void AlignRowBaselines(List<Placement> placements)
        {
            var groups = placements
                .Where(p => p.RowSpan == 1)
                .Select(p => (Placement: p, Kind: BaselineKind(ResolveSelfAlignment(p.Box.AlignSelf.ToString(), _gridBox.AlignItems.ToString()))))
                .Where(x => x.Kind is not null)
                .GroupBy(x => (x.Placement.RowStart, Last: x.Kind!.Value));

            foreach (var group in groups)
            {
                var spans = group.Select(x => (x.Placement.Box, Span: ItemRowBaseline(x.Placement.Box, group.Key.Last))).ToList();
                if (spans.Count < 2) continue;   // a lone item already sits on its own baseline

                var maxAbove = spans.Max(s => s.Span.Above);
                var maxBelow = spans.Max(s => s.Span.Below);
                foreach (var (box, span) in spans)
                {
                    var dy = group.Key.Last ? -(maxBelow - span.Below) : maxAbove - span.Above;
                    if (Math.Abs(dy) > 0.01) box.OffsetTop(dy);
                }
            }
        }

        /// <summary>The size a row needs for each baseline-sharing group in it: the furthest ascent plus the furthest descent among its items.</summary>
        private double BaselineGroupSize(IEnumerable<CssBox> items, bool last)
        {
            double above = 0, below = 0;
            foreach (var item in items)
            {
                var span = ItemRowBaseline(item, last);
                above = Math.Max(above, span.Above);
                below = Math.Max(below, span.Below);
            }

            return above + below;
        }

        /// <summary>
        /// The first (or last) baseline of an item in the column axis, as its distances to the left and right edges of a margin box <paramref name="width"/> wide.
        /// An orthogonal item has lines across this axis: its baseline is the central baseline of its first (last) line, one half line thickness inside
        /// the border box edge the lines start from (css-writing-modes-4 section 4.1). Any other item has no baseline here, and one is synthesized at
        /// the border box edge the alignment falls back to - the inline-start edge for a first baseline, the inline-end edge for a last one.
        /// </summary>
        private BaselineSpan ItemColumnBaseline(CssBox box, bool last, double width)
        {
            var mode = box.WritingMode.Value;
            if (mode.IsVertical() && !_vertical)
            {
                var linesStartLeft = mode is WritingMode.VerticalLr or WritingMode.SidewaysLr;
                var line = last ? FindLastLineBox(box) : BaselineAlignment.FindFirstLineBox(box);
                var word = line is null || line.Words.Count == 0 ? null : last ? line.Words[^1] : line.Words[0];
                var half = (word?.OwnerBox.ActualLineHeight ?? 0) / 2;
                var fromLeft = (box.ActualMarginLeft + box.ActualBorderLeftWidth + box.ActualPaddingLeft) + half;
                var fromRight = (box.ActualMarginRight + box.ActualBorderRightWidth + box.ActualPaddingRight) + half;

                // The first line is at the block-start side, the last at the block-end side.
                var onLeft = linesStartLeft != last;
                return onLeft ? new BaselineSpan(fromLeft, width - fromLeft) : new BaselineSpan(width - fromRight, fromRight);
            }

            var onLeftEdge = last == _rtl;
            return onLeftEdge
                ? new BaselineSpan(box.ActualMarginLeft, width - box.ActualMarginLeft)
                : new BaselineSpan(width - box.ActualMarginRight, box.ActualMarginRight);
        }

        /// <summary>Whether the edge a baseline alignment of an item falls back to is the left one. An orthogonal item shares a baseline only with items whose
        /// lines stack the same way, and falls back to its own block-start (first) or block-end (last) side; any other item to the grid's inline-start or end.</summary>
        private bool ColumnBaselineEdgeIsLeft(CssBox box, bool last)
        {
            var mode = box.WritingMode.Value;
            return mode.IsVertical() && !_vertical
                ? (mode is WritingMode.VerticalLr or WritingMode.SidewaysLr) != last
                : last == _rtl;
        }

        /// <summary>The <c>justify-self</c> keyword that puts an item at the edge <see cref="ColumnBaselineEdgeIsLeft"/> names (rtl mirrors start and end).</summary>
        private string ColumnBaselineFallback(CssBox box, bool last) =>
            ColumnBaselineEdgeIsLeft(box, last) != _rtl ? Keywords.Start : Keywords.End;

        /// <summary>The width each baseline-sharing group of single-column items needs in its column: the furthest baseline from the left edge plus the
        /// furthest from the right edge among them.</summary>
        private double ColumnBaselineGroupSize(IEnumerable<(CssBox Box, double Width)> items, bool last)
        {
            double above = 0, below = 0;
            foreach (var (box, width) in items)
            {
                var span = ItemColumnBaseline(box, last, width);
                above = Math.Max(above, span.Above);
                below = Math.Max(below, span.Below);
            }

            return above + below;
        }

        /// <summary>
        /// Aligns the single-column items of each column whose used <c>justify-self</c>/<c>justify-items</c> is <c>baseline</c> or <c>last baseline</c>
        /// (css-align-3 section 9.3): each was placed at the edge the alignment falls back to, and moves inwards by how far its baseline is short of
        /// the furthest one in its group.
        /// </summary>
        private void AlignColumnBaselines(List<(Placement Placement, bool Last)> items)
        {
            foreach (var group in items.GroupBy(i => (i.Placement.ColStart, i.Last, Left: ColumnBaselineEdgeIsLeft(i.Placement.Box, i.Last))))
            {
                var members = group.Select(i => i.Placement.Box).ToList();
                if (members.Count < 2) continue;   // a lone item already sits on its own baseline

                var last = group.Key.Last;
                var spans = members.Select(b => (Box: b, Span: ItemColumnBaseline(b, last, b.ActualBoxSizingWidth + b.ActualMarginLeft + b.ActualMarginRight))).ToList();
                var fromLeft = group.Key.Left;
                var furthest = spans.Max(x => fromLeft ? x.Span.Above : x.Span.Below);
                foreach (var (box, span) in spans)
                {
                    var dx = furthest - (fromLeft ? span.Above : span.Below);
                    if (dx > 0.01) box.OffsetLeft(fromLeft ? dx : -dx);
                }
            }
        }
    }
}
