using PeachPDF.Html.Core.Utils;

namespace PeachPDF.Html.Core.Dom
{
    /// <summary>
    /// First-baseline discovery shared by the flex (<see cref="CssLayoutEngineFlex"/>) and grid
    /// (<see cref="CssLayoutEngineGrid"/>) engines for <c>baseline</c> box alignment. An item's baseline is
    /// the first font baseline of its content (CSS Box Alignment §9 / Flexbox §8.5) — the first in-flow line
    /// box in document order.
    /// </summary>
    internal static class BaselineAlignment
    {
        /// <summary>
        /// Finds the first line box within <paramref name="box"/>'s subtree, in document order (depth-first,
        /// skipping out-of-flow and <c>display:none</c> descendants).
        /// </summary>
        public static CssLineBox? FindFirstLineBox(CssBox box)
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
        /// Computes an item's baseline offset from its own start (top) edge — the first font baseline of its
        /// content. Returns null if the item has no line-box content anywhere, in which case the caller falls
        /// back to start alignment.
        /// </summary>
        public static double? GetItemBaselineOffset(CssBox box)
        {
            var lineBox = FindFirstLineBox(box);
            if (lineBox == null) return null;

            var word = CssBox.FirstWordOccurence(lineBox.OwnerBox, lineBox);
            if (word == null) return null;

            return (word.Top - box.Location.Y) + word.OwnerBox.ActualFont.Ascent;
        }
    }
}
