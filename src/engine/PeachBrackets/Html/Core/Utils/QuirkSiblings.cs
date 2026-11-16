using System.Collections.Generic;
using PeachBrackets.Html.Dom;

namespace PeachBrackets.Html.Utils
{
    /// <summary>
    /// The "substantial" and "blank" tests of the HTML Standard's margin-collapsing quirks (15.3.9): a node is substantial when it is an element or a text
    /// node that is not inter-element whitespace, and an element is blank when it contains no substantial node. They differ from the structural
    /// pseudo-classes in counting text, which <c>:first-child</c> and <c>:empty</c> do not.
    /// </summary>
    internal static class QuirkSiblings
    {
        /// <summary>
        /// Whether <paramref name="box"/> has a substantial sibling before it (<paramref name="following"/> false) or after it.
        /// </summary>
        /// <remarks>Stops at the first substantial sibling, so a long run of siblings costs one step each, not a walk of the run.</remarks>
        internal static bool HasSubstantialSibling(CssBox box, bool following)
        {
            var parent = box.ParentBox;
            while (parent is not null && parent.IsAnonymousWrapper) parent = parent.ParentBox;
            if (parent is null) return false;

            // The nodes after the box are walked from the box on, rather than from the start of the parent: a run of siblings each asking this
            // would otherwise cost the square of its length.
            var children = parent.Boxes;
            var index = IndexOfWithin(children, box);
            if (following && index >= 0)
            {
                var sawBox = true;
                for (var i = index; i < children.Count; i++)
                {
                    if (Visit(children[i], box, ref sawBox, following, out var substantial))
                        return substantial;
                }

                return false;
            }

            var seen = false;
            foreach (var child in children)
            {
                if (Visit(child, box, ref seen, following, out var substantial))
                    return substantial;
            }

            return false;
        }

        // The box's own position among the children, or -1 when it sits inside an anonymous wrapper (the walk then starts from the beginning).
        private static int IndexOfWithin(IReadOnlyList<CssBox> children, CssBox box)
        {
            for (var i = 0; i < children.Count; i++)
            {
                if (ReferenceEquals(children[i], box)) return i;
            }

            return -1;
        }

        // True when the walk is decided: either a substantial node was found on the wanted side (substantial = true) or the box was reached
        // with nothing substantial before it, for the "previous" question (substantial = false).
        private static bool Visit(CssBox node, CssBox target, ref bool sawTarget, bool following, out bool substantial)
        {
            substantial = false;

            if (ReferenceEquals(node, target))
            {
                sawTarget = true;
                // Before the target nothing was substantial: the answer to "is one before it" is no.
                return !following;
            }

            if (IsGenerated(node)) return false;

            if (node.IsAnonymousWrapper)
            {
                foreach (var inner in node.Boxes)
                {
                    if (Visit(inner, target, ref sawTarget, following, out substantial))
                        return true;
                }

                return false;
            }

            if (!IsSubstantial(node)) return false;

            // A substantial node on the wanted side of the target decides it; one on the other side does not matter.
            if (sawTarget == following)
            {
                substantial = true;
                return true;
            }

            return false;
        }

        // Generated content is not a node of the document; a ::first-letter box is, since it holds text of the source.
        private static bool IsGenerated(CssBox node) =>
            node.IsBeforePseudoElement || node.IsAfterPseudoElement || node.IsMarkerPseudoElement || node.IsPlaceholderPseudoElement;

        /// <summary>An element, or a text node that is not collapsible whitespace.</summary>
        internal static bool IsSubstantial(CssBox node) =>
            node.HtmlTag is not null || !HtmlUtils.IsNullOrCollapsibleWhitespace(node.Text);

        /// <summary>Whether <paramref name="box"/> contains no substantial node (HTML Standard 15.3.9's "blank").</summary>
        internal static bool IsBlank(CssBox box) => !ContainsSubstantial(box);

        private static bool ContainsSubstantial(CssBox box)
        {
            foreach (var child in box.Boxes)
            {
                if (IsGenerated(child)) continue;
                if (child.IsAnonymousWrapper)
                {
                    if (ContainsSubstantial(child)) return true;
                    continue;
                }

                if (IsSubstantial(child)) return true;
            }

            return false;
        }
    }
}
