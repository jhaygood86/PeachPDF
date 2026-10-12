using PeachPDF.Html.Core.Dom;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace PeachPDF.Html.Core
{
    /// <summary>
    /// The element children of one HTML box, indexed once so the structural selectors (<c>:nth-child</c> and its
    /// <c>-last-</c>/<c>-of-type</c> forms, the <c>+</c> and <c>~</c> combinators, <c>:has(+ S)</c>) answer "where does this
    /// element sit among its siblings" in O(1) instead of scanning <c>parent.Children</c> for every element they test.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Without it each of the N rows of a table asks the question of an N-wide parent, so a zebra-striped
    /// <c>tr:nth-child(even)</c> table costs O(N²) time and allocates an N-element list per row; at 30,000 rows that
    /// was most of the cascade. One index per parent makes it O(N) in total.
    /// </para>
    /// <para>
    /// Keyed by the parent's <see cref="CssBox.Boxes"/> list, which is mutated freely with no single choke point to
    /// invalidate from, so an index proves itself on every use instead: the child count and the first and last element must be unchanged (the
    /// cascade only ever grows that list, by synthesizing <c>::before</c>/<c>::after</c>/<c>::marker</c> boxes, and
    /// those have no tag so they are not siblings in the sense of any selector here, but they do change the count and
    /// so retire the index conservatively) and the element found at the answered position must be the one asked about.
    /// Only a list that swaps one element for another while keeping its size would go unnoticed, which nothing in the
    /// cascade does.
    /// </para>
    /// <para>
    /// Parents narrower than <see cref="MinChildren"/> are not indexed: scanning a handful of siblings is cheaper than
    /// building a map, and it keeps the common case allocation-free. SVG nodes are not indexed either, since their
    /// <c>Children</c> is rebuilt on every access and there is no stable list to key on.
    /// </para>
    /// </remarks>
    internal sealed class SiblingIndex
    {
        /// <summary>The narrowest parent worth indexing.</summary>
        internal const int MinChildren = 16;

        private static readonly ConditionalWeakTable<List<CssBox>, SiblingIndex> Cache = new();

        private readonly List<CssBox> _children;
        private readonly int _childCount;
        private readonly Dictionary<CssBox, int> _position;
        private readonly int[] _childSlot;

        // Position among same-tag siblings, and how many there are, filled on first use: only the -of-type
        // selectors need them and most documents never write one.
        // Published as one object so a reader never sees the positions without the counts.
        private TypeTables? _typeTables;
        private readonly StringComparer _tagComparer;

        private SiblingIndex(List<CssBox> children, StringComparison tagComparison)
        {
            _children = children;
            _childCount = children.Count;
            _tagComparer = StringComparer.FromComparison(tagComparison);

            var elements = new List<CssBox>(children.Count);
            var slots = new List<int>(children.Count);
            for (var i = 0; i < children.Count; i++)
            {
                if (((ICssDomNode)children[i]).TagName is null) continue;
                elements.Add(children[i]);
                slots.Add(i);
            }

            Elements = elements;
            _childSlot = slots.ToArray();
            _position = new Dictionary<CssBox, int>(elements.Count, ReferenceEqualityComparer.Instance);
            for (var i = 0; i < elements.Count; i++) _position[elements[i]] = i;
        }

        /// <summary>
        /// Whether the list still looks like the one indexed: same size, and the first and last element still where they
        /// were. Neither check proves the middle unchanged, which is why <see cref="IndexOf"/> checks each answer too.
        /// </summary>
        private bool StillDescribes(List<CssBox> list)
        {
            if (list.Count != _childCount) return false;
            if (Elements.Count == 0) return true;

            var first = _childSlot[0];
            var last = _childSlot[^1];
            return ReferenceEquals(list[first], Elements[0]) && ReferenceEquals(list[last], Elements[^1]);
        }

        /// <summary>The element children (those with a tag) in document order.</summary>
        internal List<CssBox> Elements { get; }

        /// <summary>
        /// The index for <paramref name="children"/>, or <c>null</c> when it is not worth having or cannot be kept:
        /// not an HTML box's own child list, or too short to matter.
        /// </summary>
        internal static SiblingIndex? For(IReadOnlyList<ICssDomNode> children, StringComparison tagComparison)
        {
            if (children is not List<CssBox> list || list.Count < MinChildren) return null;

            if (Cache.TryGetValue(list, out var index) && index.StillDescribes(list)) return index;

            index = new SiblingIndex(list, tagComparison);
            Cache.AddOrUpdate(list, index);
            return index;
        }

        /// <summary>
        /// <paramref name="node"/>'s position among the elements, or -1 when it is not one of them (a text node, or
        /// not a child of this parent).
        /// </summary>
        internal int IndexOf(ICssDomNode node)
        {
            if (node is not CssBox box || !_position.TryGetValue(box, out var at)) return -1;

            // Proves the index still describes the list: the child must still sit where it was indexed.
            var slot = _childSlot[at];
            return slot < _children.Count && ReferenceEquals(_children[slot], box) ? at : -1;
        }

        /// <summary>
        /// <paramref name="node"/>'s 0-based position among the siblings sharing its tag name, and how many such
        /// siblings there are; <c>(-1, 0)</c> when it is not an element of this parent.
        /// </summary>
        internal (int Position, int Count) TypePositionOf(ICssDomNode node)
        {
            var at = IndexOf(node);
            if (at < 0) return (-1, 0);

            var tables = _typeTables;
            if (tables is null)
            {
                var counts = new Dictionary<string, int>(_tagComparer);
                var positions = new int[Elements.Count];

                for (var i = 0; i < Elements.Count; i++)
                {
                    var tag = ((ICssDomNode)Elements[i]).TagName!;
                    counts.TryGetValue(tag, out var seen);
                    positions[i] = seen;
                    counts[tag] = seen + 1;
                }

                tables = _typeTables = new TypeTables(positions, counts);
            }

            return (tables.Positions[at], tables.Counts[((ICssDomNode)node).TagName!]);
        }

        private sealed record TypeTables(int[] Positions, Dictionary<string, int> Counts);
    }
}
