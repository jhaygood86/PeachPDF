using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace PeachPDF.SourceGenerators.Model
{
    /// <summary>
    /// Wraps an <see cref="ImmutableArray{T}"/> with structural (value) equality, so it's safe to use
    /// as part of an <see cref="Microsoft.CodeAnalysis.IIncrementalGenerator"/> pipeline value —
    /// <see cref="ImmutableArray{T}"/>'s own default <c>Equals</c> is reference/identity based, which
    /// would make every pipeline stage downstream of one "different" on every run and defeat caching.
    /// </summary>
    internal readonly struct EquatableArray<T> : IEquatable<EquatableArray<T>>, IReadOnlyList<T>
        where T : IEquatable<T>
    {
        private readonly ImmutableArray<T> _items;

        public EquatableArray(ImmutableArray<T> items) => _items = items;

        public static EquatableArray<T> Empty { get; } = new(ImmutableArray<T>.Empty);

        public int Count => _items.IsDefault ? 0 : _items.Length;

        public T this[int index] => _items[index];

        public bool Equals(EquatableArray<T> other)
        {
            var a = _items.IsDefault ? ImmutableArray<T>.Empty : _items;
            var b = other._items.IsDefault ? ImmutableArray<T>.Empty : other._items;
            return a.SequenceEqual(b);
        }

        public override bool Equals(object? obj) => obj is EquatableArray<T> other && Equals(other);

        public override int GetHashCode()
        {
            if (_items.IsDefault) return 0;
            var hash = 17;
            foreach (var item in _items) hash = hash * 31 + (item?.GetHashCode() ?? 0);
            return hash;
        }

        public IEnumerator<T> GetEnumerator() =>
            ((IEnumerable<T>)(_items.IsDefault ? ImmutableArray<T>.Empty : _items)).GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public static bool operator ==(EquatableArray<T> left, EquatableArray<T> right) => left.Equals(right);
        public static bool operator !=(EquatableArray<T> left, EquatableArray<T> right) => !left.Equals(right);
    }

    internal static class EquatableArray
    {
        public static EquatableArray<T> ToEquatableArray<T>(this IEnumerable<T> source) where T : IEquatable<T> =>
            new(source.ToImmutableArray());
    }
}
