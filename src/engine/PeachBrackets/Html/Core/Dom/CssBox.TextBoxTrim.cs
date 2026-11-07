using System;
using PeachBrackets.Css;
using PeachBrackets.Html.Utils;

namespace PeachBrackets.Html.Dom
{
    /// <summary><c>text-box-trim</c> / <c>text-box-edge</c> (CSS Inline 3 §5): cutting the leading off a block container's first and last formatted line.</summary>
    internal partial class CssBox
    {
        /// <summary>
        /// Trims the space above the first formatted line and below the last one of this block container, down to the
        /// <c>text-box-edge</c> metrics of the font each line is set in. The content moves up by what is cut from the top, and the container
        /// is shorter by both cuts (an explicit height is applied afterwards and wins). Run once, after the content is complete and before
        /// the height is resolved.
        /// </summary>
        private void ApplyTextBoxTrim()
        {
            if (!IsTrimContainer()) return;

            var trim = RhythmGrammar.ParseTrim(TextBoxTrim);
            if (trim == global::PeachBrackets.Css.TextBoxTrim.None) return;

            var edge = RhythmGrammar.ParseEdge(_computedStyle.Text.TextBoxEdge);
            var top = 0.0;
            var bottom = 0.0;

            if ((trim & global::PeachBrackets.Css.TextBoxTrim.Start) != 0 && FindEdgeLine(this, first: true) is { } firstLine)
                top = TrimOf(firstLine, edge, over: true);

            if ((trim & global::PeachBrackets.Css.TextBoxTrim.End) != 0 && FindEdgeLine(this, first: false) is { } lastLine)
                bottom = TrimOf(lastLine, edge, over: false);

            if (top > 0)
            {
                foreach (var child in Boxes)
                {
                    if (child.IsAbsolutelyPositioned || child.DerivedStyle.ActualDisplay == Keywords.None) continue;
                    child.OffsetTop(-top);
                }

                OffsetOwnLineBoxesTop(-top);
            }

            ActualBottom -= top + bottom;
        }

        private bool IsTrimContainer() =>
            DerivedStyle.ActualDisplay is Keywords.Block or Keywords.FlowRoot or Keywords.ListItem or Keywords.InlineBlock
            && !WritingModeFrame.For(this).IsVertical;

        /// <summary>The first (or last) line box of the first (or last) in-flow block descendant that has lines, or of this box itself.</summary>
        private static CssLineBox? FindEdgeLine(CssBox box, bool first)
        {
            if (box.LineBoxes.Count > 0)
            {
                var line = first ? box.LineBoxes[0] : box.LineBoxes[^1];
                return line.BaselineExtent is null || line.Words.Count == 0 ? null : line;
            }

            for (var i = 0; i < box.Boxes.Count; i++)
            {
                var child = box.Boxes[first ? i : box.Boxes.Count - 1 - i];
                if (child.IsInline || child.IsOutOfFlow || child.DerivedStyle.ActualDisplay == Keywords.None) continue;

                // A table, flex or grid container formats its content its own way; its lines are not this container's first line.
                if (child.DerivedStyle.ActualDisplay is not (Keywords.Block or Keywords.FlowRoot or Keywords.ListItem)) return null;

                return FindEdgeLine(child, first);
            }

            return null;
        }

        /// <summary>How much of <paramref name="line"/>'s extent lies beyond the edge metric of its owner's font.</summary>
        private static double TrimOf(CssLineBox line, TextBoxEdge edge, bool over)
        {
            if (line.BaselineExtent is not { } extent) return 0;

            var font = line.OwnerBox.ActualFont;
            var metric = over ? edge.Over : edge.Under;
            double reach;

            if (over)
            {
                reach = metric switch
                {
                    TextEdgeMetric.Cap => FontMetricMeasurement.Ratio(font, FontMetric.Cap) * font.Size,
                    TextEdgeMetric.Ex => FontMetricMeasurement.Ratio(font, FontMetric.Ex) * font.Size,
                    _ => font.Ascent
                };
                // auto is the font's own text edge, which leaves only the leading to cut.
                return Math.Max(0, extent.AboveBaseline - (edge.IsAuto ? font.Ascent : reach));
            }

            reach = metric == TextEdgeMetric.Alphabetic ? 0 : font.Height - font.Ascent;
            return Math.Max(0, extent.BelowBaseline - (edge.IsAuto ? font.Height - font.Ascent : reach));
        }
    }
}
