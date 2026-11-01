using PeachBrackets.Html.Dom;
using PeachBrackets.Text;
using PeachDrawing.Core;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PeachBrackets.Html.TextGeometry
{
    /// <summary>
    /// Turns the text runs of a laid-out document into the nodes of an accessibility tree: one for each block of text (per page it is on),
    /// with a child for each link inside it.
    /// </summary>
    internal static class AccessibilityCollector
    {
        public static IReadOnlyList<AccessibleNode> Collect(IReadOnlyList<TextRun> runs)
        {
            var nodes = new List<AccessibleNode>();
            var start = 0;
            while (start < runs.Count)
            {
                // The runs of one block on one page: consecutive, with the same block and page.
                var end = start + 1;
                while (end < runs.Count && runs[end].BlockId == runs[start].BlockId && runs[end].PageIndex == runs[start].PageIndex)
                    end++;

                AddBlock(runs, start, end, nodes);
                start = end;
            }

            return nodes;
        }

        private static void AddBlock(IReadOnlyList<TextRun> runs, int start, int end, List<AccessibleNode> nodes)
        {
            var text = ReadingText(runs, start, end);
            if (text.Length == 0)
                return;

            var (role, level) = RoleOf(runs[start]);
            var blockIndex = nodes.Count;
            nodes.Add(new AccessibleNode(role, level, text, runs[start].PageIndex, Union(runs, start, end), null, -1));

            // A link is the consecutive runs that lead to the same place.
            var i = start;
            while (i < end)
            {
                if (runs[i].LinkUri is not { } uri)
                {
                    i++;
                    continue;
                }

                var j = i + 1;
                while (j < end && runs[j].LinkUri == uri)
                    j++;

                var linkText = ReadingText(runs, i, j);
                if (linkText.Length > 0)
                    nodes.Add(new AccessibleNode(AccessibleRole.Link, 0, linkText, runs[i].PageIndex, Union(runs, i, j), uri, blockIndex));

                i = j;
            }
        }

        // The text of some runs as it is read: the pieces of one line (text and links are separate runs) run together as they were written, one
        // line follows another after a space, and any other white space is a single space.
        private static string ReadingText(IReadOnlyList<TextRun> runs, int start, int end)
        {
            var text = new System.Text.StringBuilder();
            for (var i = start; i < end; i++)
            {
                if (i > start && runs[i].LineId != runs[i - 1].LineId)
                    text.Append(' ');

                text.Append(runs[i].Text);
            }

            return System.Text.RegularExpressions.Regex.Replace(text.ToString(), @"\s+", " ").Trim();
        }

        // What the element the text was written in is: a heading, a list item, or plain text.
        private static (AccessibleRole Role, int Level) RoleOf(TextRun run)
        {
            if (run.Slices.Count == 0)
                return (AccessibleRole.Text, 0);

            for (var box = run.Slices[0].Word.OwnerBox; box is not null; box = box.ParentBox)
            {
                if (box.HtmlTag is not { } tag)
                    continue;

                var name = tag.Name;
                if (name.Length == 2 && (name[0] is 'h' or 'H') && name[1] is >= '1' and <= '6')
                    return (AccessibleRole.Heading, name[1] - '0');

                if (name.Equals("li", StringComparison.OrdinalIgnoreCase))
                    return (AccessibleRole.ListItem, 0);

                if (name.Equals("p", StringComparison.OrdinalIgnoreCase) || name.Equals("div", StringComparison.OrdinalIgnoreCase))
                    break;
            }

            return (AccessibleRole.Text, 0);
        }

        private static Rect Union(IReadOnlyList<TextRun> runs, int start, int end)
        {
            var left = double.MaxValue;
            var top = double.MaxValue;
            var right = double.MinValue;
            var bottom = double.MinValue;
            for (var i = start; i < end; i++)
            {
                var bounds = runs[i].Bounds;
                left = Math.Min(left, bounds.Left);
                top = Math.Min(top, bounds.Top);
                right = Math.Max(right, bounds.Right);
                bottom = Math.Max(bottom, bounds.Bottom);
            }

            return new Rect(left, top, right - left, bottom - top);
        }
    }
}
