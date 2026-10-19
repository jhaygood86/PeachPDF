using PeachPDF.Html.Core.Fragments;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace PeachPDF.Html.Core.Dom
{
    /// <summary>
    /// The boxes a column of a multi-column container does <b>not</b> hold, read off the break record it stopped at: every child
    /// from where the flow resumes, at each level of the chain, and the floats and table cells that move on whole.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Kept as ranges of child lists rather than as a set of boxes. The set of everything after the break is the rest of the
    /// document, and it was built, queried and walked once per column, so a long container paid for all of the document it
    /// had not reached on every one of its columns: quadratic in pages. The questions asked of it are only ever "is this box
    /// beyond", of the few boxes a column does hold, so they are answered by looking the box up in its parent's list.
    /// </para>
    /// </remarks>
    internal sealed class BoxesBeyondColumn : IBoxExclusion
    {
        /// <summary>No box is beyond: the answer for the last column of a container and for every column of one that fits.</summary>
        internal static readonly BoxesBeyondColumn None = new();

        private HashSet<CssBox>? _boxes;
        private List<(List<CssBox> Owner, int From)>? _ranges;

        /// <summary>Whether nothing is beyond.</summary>
        internal bool IsEmpty => _boxes is null && _ranges is null;

        /// <summary>Adds one box that moves on whole.</summary>
        internal void Add(CssBox box) => (_boxes ??= []).Add(box);

        /// <summary>Adds every box of <paramref name="owner"/> from <paramref name="from"/> on.</summary>
        internal void AddFrom(List<CssBox> owner, int from)
        {
            if (from < owner.Count) (_ranges ??= []).Add((owner, from));
        }

        /// <inheritdoc />
        public bool Contains(CssBox box)
        {
            if (_boxes is not null && _boxes.Contains(box)) return true;

            if (_ranges is not null)
            {
                foreach (var (owner, from) in _ranges)
                {
                    if (ChildIndex.TryGet(owner, box, out var index) && index >= from) return true;
                }
            }

            return false;
        }

        /// <summary>Every box that is beyond, for a check against the set this stands in for; not used by layout.</summary>
        internal IEnumerable<CssBox> Enumerate()
        {
            if (_boxes is not null)
            {
                foreach (var box in _boxes) yield return box;
            }

            if (_ranges is not null)
            {
                foreach (var (owner, from) in _ranges)
                {
                    for (var i = from; i < owner.Count; i++) yield return owner[i];
                }
            }
        }

        /// <summary>
        /// Where a box sits in a child list, found by lookup instead of by scan, for lists long enough that a scan is the
        /// cost. One map per list, rebuilt when the list changes.
        /// </summary>
        /// <remarks>
        /// Like the sibling index, it proves itself on use: the list must be the size it was mapped at, with the same first and
        /// last child, and a hit must still be where it was. A box mapped to its last occurrence, since "beyond" asks whether
        /// <i>any</i> occurrence is at or after the resume point.
        /// </remarks>
        private sealed class ChildIndex
        {
            private static readonly ConditionalWeakTable<List<CssBox>, ChildIndex> Cache = new();

            private readonly int _count;
            private readonly CssBox? _first;
            private readonly CssBox? _last;
            private readonly Dictionary<CssBox, int> _index;

            private ChildIndex(List<CssBox> list)
            {
                _count = list.Count;
                _first = list.Count > 0 ? list[0] : null;
                _last = list.Count > 0 ? list[^1] : null;
                _index = new Dictionary<CssBox, int>(list.Count, ReferenceEqualityComparer.Instance);

                for (var i = 0; i < list.Count; i++) _index[list[i]] = i;
            }

            private bool Describes(List<CssBox> list) =>
                list.Count == _count
                && (_count == 0 || (ReferenceEquals(list[0], _first) && ReferenceEquals(list[^1], _last)));

            internal static bool TryGet(List<CssBox> list, CssBox box, out int index)
            {
                if (!Cache.TryGetValue(list, out var map) || !map.Describes(list))
                {
                    map = new ChildIndex(list);
                    Cache.AddOrUpdate(list, map);
                }

                if (!map._index.TryGetValue(box, out index)) return false;

                if (index < list.Count && ReferenceEquals(list[index], box)) return true;

                // The list moved under the map without changing size or ends: map it afresh and ask again.
                map = new ChildIndex(list);
                Cache.AddOrUpdate(list, map);

                return map._index.TryGetValue(box, out index);
            }
        }
    }
}
