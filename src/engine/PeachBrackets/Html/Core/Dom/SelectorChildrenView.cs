using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace PeachBrackets.Html.Dom
{
    /// <summary>
    /// The children a selector sees for one box: its boxes with each anonymous wrapper replaced by what it holds, so elements layout moved
    /// into a wrapper are siblings of the ones beside it again. Remembered per parent, because the structural selectors ask for it once per
    /// element they test - a zebra-striped table asks its N-wide <c>tbody</c> N times - and looking for a wrapper, let alone flattening, is
    /// a walk over every child.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Without a wrapper the answer is the box's own <see cref="CssBox.Boxes"/> list, which is also what <see cref="SiblingIndex"/> is keyed
    /// on; with one it is a flattened <see cref="List{T}"/> of boxes that is built once and never changed, so the same index can be kept for
    /// it as well. The list is a <c>List&lt;CssBox&gt;</c> for that reason, not a list of the interface.
    /// </para>
    /// <para>
    /// Keyed by the parent's <see cref="CssBox.Boxes"/> list, which is mutated freely with no single choke point to invalidate from, so a
    /// view proves itself on every use as <see cref="SiblingIndex"/> does: the child count and the first and last child unchanged, and
    /// each box that is or could become a wrapper (an anonymous box with no source element, which holds nothing yet) holding as many
    /// children as when it was looked at. Only a list that swaps one middle child for another while keeping its size would go unnoticed,
    /// which nothing in the cascade or in layout does. Parents narrower than <see cref="SiblingIndex.MinChildren"/> are answered without
    /// keeping anything: looking at a handful of children is cheaper than remembering them.
    /// </para>
    /// </remarks>
    internal sealed class SelectorChildrenView
    {
        private static readonly ConditionalWeakTable<List<CssBox>, SelectorChildrenView> Cache = [];

        private readonly int _count;
        private readonly CssBox? _first;
        private readonly CssBox? _last;

        // Every anonymous box met on the way (at any depth), with the number of children it had.
        private readonly (CssBox Box, int Count)[] _watched;

        // Null when nothing in the list is a wrapper: the answer is then the list itself.
        private readonly List<CssBox>? _flat;

        private SelectorChildrenView(List<CssBox> boxes, (CssBox, int)[] watched, List<CssBox>? flat)
        {
            _count = boxes.Count;
            _first = boxes.Count > 0 ? boxes[0] : null;
            _last = boxes.Count > 0 ? boxes[^1] : null;
            _watched = watched;
            _flat = flat;
        }

        /// <summary>The children a selector sees for the box whose own children are <paramref name="boxes"/>.</summary>
        internal static IReadOnlyList<ICssDomNode> For(List<CssBox> boxes)
        {
            if (boxes.Count < SiblingIndex.MinChildren)
            {
                // Nothing is kept for a narrow parent, so with no wrapper to flatten there is nothing to build either.
                var anyWrapper = false;
                foreach (var child in boxes)
                {
                    if (child.IsAnonymousWrapper) { anyWrapper = true; break; }
                }

                return anyWrapper ? Build(boxes).Children(boxes) : boxes;
            }

            if (Cache.TryGetValue(boxes, out var view) && view.StillDescribes(boxes)) return view.Children(boxes);

            view = Build(boxes);
            Cache.AddOrUpdate(boxes, view);
            return view.Children(boxes);
        }

        private IReadOnlyList<ICssDomNode> Children(List<CssBox> boxes) => _flat ?? (IReadOnlyList<ICssDomNode>)boxes;

        private bool StillDescribes(List<CssBox> boxes)
        {
            if (boxes.Count != _count) return false;
            if (_count > 0 && (!ReferenceEquals(boxes[0], _first) || !ReferenceEquals(boxes[^1], _last))) return false;

            foreach (var (box, count) in _watched)
                if (box.Boxes.Count != count) return false;

            return true;
        }

        private static SelectorChildrenView Build(List<CssBox> boxes)
        {
            var watched = new List<(CssBox, int)>();
            var hasWrapper = false;

            foreach (var child in boxes)
            {
                if (child.IsAnonymousWrapper) { hasWrapper = true; break; }
            }

            if (!hasWrapper)
            {
                // Nothing to flatten, but a box with no source element and no children yet is one a later pass may fill.
                foreach (var child in boxes)
                    if (child.IsAnonymousBox) watched.Add((child, child.Boxes.Count));

                return new SelectorChildrenView(boxes, [.. watched], null);
            }

            var flat = new List<CssBox>(boxes.Count);
            Collect(boxes);
            return new SelectorChildrenView(boxes, [.. watched], flat);

            void Collect(List<CssBox> level)
            {
                foreach (var child in level)
                {
                    if (child.IsAnonymousWrapper)
                    {
                        watched.Add((child, child.Boxes.Count));
                        Collect(child.Boxes);
                    }
                    else
                    {
                        if (child.IsAnonymousBox) watched.Add((child, child.Boxes.Count));
                        flat.Add(child);
                    }
                }
            }
        }
    }
}
