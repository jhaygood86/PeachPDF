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
    }
}
