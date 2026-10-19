using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace PeachPDF.Html.Core.Dom
{
    /// <summary>
    /// Remembers what changing one property of a shared style gave, so the next box that makes the same change to the same style
    /// is handed the same result instead of a copy of its own.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A box's style is a <see cref="ComputedStyle"/> of 16 immutable area records, and every property the cascade sets clones the
    /// area it lives in and then the <see cref="ComputedStyle"/> that points at it. Thousands of boxes of one kind make the same
    /// sequence of changes from the same starting point, so they ended up with thousands of equal but separate objects: for a
    /// 14,000-box document 14,403 <see cref="BorderArea"/>s of which 2 were different, and 14,403 <see cref="ComputedStyle"/>s of
    /// which 16 were. Each of those is also a clone made, and discarded, for every property set along the way.
    /// </para>
    /// <para>
    /// A transition is keyed by the style it starts from and the setter doing the change (the <c>static</c> lambda at the call
    /// site, whose delegate is one object per site), then by the new value. Only styles that took part from the start are
    /// followed - the shared <c>Default</c> of each record, and anything this cache produced - so a style that left that world (a
    /// box with custom properties, say, whose dictionary is mutated in place and so must never be shared) is simply cloned as
    /// before, and a chain of changes unique to one box costs nothing extra.
    /// </para>
    /// <para>
    /// One cache per document, owned by its <see cref="HtmlContainerInt"/>: nothing outlives the document, one document's unusual
    /// values cannot crowd out another's, and the cascade that uses it is single-threaded. It is bounded, and past the bound it
    /// stops remembering and clones.
    /// </para>
    /// </remarks>
    internal sealed class StyleTransitionCache
    {
        /// <summary>How many transitions one document remembers.</summary>
        internal const int MaxTransitions = 200_000;

        private static readonly ConcurrentDictionary<object, byte> Roots = new(ReferenceEqualityComparer.Instance);

        private readonly HashSet<object> _members = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<(object Source, Delegate Apply), object> _tables = new(new TableKeyComparer());
        private int _transitions;

        /// <summary>
        /// Marks <paramref name="instance"/> - the shared default of a style record - as somewhere sharing starts from.
        /// </summary>
        internal static T Root<T>(T instance) where T : class
        {
            Roots.TryAdd(instance, 0);
            return instance;
        }

        /// <summary>How many transitions are remembered.</summary>
        internal int Transitions => _transitions;

        private bool Participates(object instance) => Roots.ContainsKey(instance) || _members.Contains(instance);

        /// <summary>
        /// <paramref name="self"/> with one property changed to <paramref name="next"/>, as <c>apply</c> makes it - the same instance
        /// as the last time the same change was made to <paramref name="self"/>.
        /// </summary>
        internal TSelf Set<TSelf, TValue>(TSelf self, TValue current, TValue next, Func<TSelf, TValue, TSelf> apply)
            where TSelf : class
        {
            if (EqualityComparer<TValue>.Default.Equals(current, next)) return self;

            // A null value cannot key a dictionary, and a style outside the shared world is not followed.
            if (next is null || _transitions >= MaxTransitions || !Participates(self)) return apply(self, next);

            // `next` was checked for null above; the compiler cannot carry that into the type argument.
#pragma warning disable CS8714
            if (!_tables.TryGetValue((self, apply), out var table))
            {
                table = new Dictionary<TValue, TSelf>();
                _tables.Add((self, apply), table);
            }

            var byValue = (Dictionary<TValue, TSelf>)table;

            if (byValue.TryGetValue(next, out var shared)) return shared;
#pragma warning restore CS8714

            var result = apply(self, next);
            byValue.Add(next, result);
            _members.Add(result);
            _transitions++;

            return result;
        }

        /// <summary>
        /// <paramref name="self"/> with its area <paramref name="current"/> replaced by <paramref name="next"/>, as <c>apply</c> makes
        /// it - the same instance as the last time the same area was put in the same style.
        /// </summary>
        internal TSelf Adopt<TSelf, TArea>(TSelf self, TArea current, TArea next, Func<TSelf, TArea, TSelf> apply)
            where TSelf : class
            where TArea : class
        {
            if (ReferenceEquals(current, next)) return self;

            if (_transitions >= MaxTransitions || !Participates(self)) return apply(self, next);

            if (!_tables.TryGetValue((self, apply), out var table))
            {
                table = new Dictionary<TArea, TSelf>(ReferenceEqualityComparer.Instance);
                _tables.Add((self, apply), table);
            }

            var byArea = (Dictionary<TArea, TSelf>)table;

            if (byArea.TryGetValue(next, out var shared)) return shared;

            var result = apply(self, next);
            byArea.Add(next, result);
            _members.Add(result);
            _transitions++;

            return result;
        }

        private sealed class TableKeyComparer : IEqualityComparer<(object Source, Delegate Apply)>
        {
            public bool Equals((object Source, Delegate Apply) x, (object Source, Delegate Apply) y) =>
                ReferenceEquals(x.Source, y.Source) && ReferenceEquals(x.Apply, y.Apply);

            public int GetHashCode((object Source, Delegate Apply) key) =>
                HashCode.Combine(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(key.Source),
                    System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(key.Apply));
        }
    }
}
