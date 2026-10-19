using System;
using System.Collections;
using System.Collections.Frozen;
using System.Collections.Generic;

namespace PeachBrackets.Css
{
    /// <summary>
    /// A frozen, case-insensitive keyword-to-value table (the <see cref="Map"/> fields): what a <c>FrozenDictionary&lt;string, T&gt;</c>
    /// over a CSS enum was, without instantiating the framework's dictionary types over each of the ~120 enums.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A framework generic instantiated over a value type of this assembly cannot be precompiled by ReadyToRun (it lives outside
    /// this assembly's version bubble), so every <c>Dictionary&lt;string, TextTransform&gt;</c> and
    /// <c>FrozenDictionary&lt;string, TextTransform&gt;</c> was JIT-compiled at startup: about 2,700 methods, roughly 60% of the
    /// time it takes a ReadyToRun build to render its first page. This type keeps the framework dictionary over <c>int</c> - one
    /// instantiation shared by every map - and the values in a plain array of this assembly's own generic type, which is
    /// precompiled for each enum.
    /// </para>
    /// <para>
    /// Built with a collection initializer (<c>new KeywordMap&lt;T&gt;(comparer) { { "none", T.None } }.Freeze()</c>) so the
    /// declarations in <see cref="Map"/> keep their old shape. Arrays are grown by hand, not through <c>List&lt;T&gt;</c> or
    /// <c>Array.Resize&lt;T&gt;</c>, for the same reason.
    /// </para>
    /// </remarks>
    internal sealed class KeywordMap<T> : IReadOnlyDictionary<string, T>, IEnumerable
    {
        private readonly StringComparer _comparer;
        private string[] _keys = new string[8];
        private T[] _values = new T[8];
        private int _count;
        private FrozenDictionary<string, int>? _index;

        public KeywordMap(StringComparer comparer)
        {
            _comparer = comparer;
        }

        /// <summary>Collection-initializer entry point; only valid before <see cref="Freeze"/>.</summary>
        public void Add(string key, T value)
        {
            if (_index is not null) throw new InvalidOperationException("The map is frozen.");

            if (_count == _keys.Length)
            {
                var keys = new string[_count * 2];
                var values = new T[_count * 2];
                Array.Copy(_keys, keys, _count);
                Array.Copy(_values, values, _count);
                _keys = keys;
                _values = values;
            }

            _keys[_count] = key;
            _values[_count] = value;
            _count++;
        }

        /// <summary>Builds the lookup index and returns this map; a duplicate key throws, as adding it to a dictionary did.</summary>
        public KeywordMap<T> Freeze()
        {
            var index = new Dictionary<string, int>(_count, _comparer);

            for (var i = 0; i < _count; i++)
                index.Add(_keys[i], i);

            _index = index.ToFrozenDictionary(_comparer);

            return this;
        }

        public int Count => _count;

        public T this[string key] =>
            TryGetValue(key, out var value) ? value : throw new KeyNotFoundException(key);

        public bool ContainsKey(string key) => _index!.ContainsKey(key);

        public bool TryGetValue(string key, out T value)
        {
            if (_index!.TryGetValue(key, out var at))
            {
                value = _values[at];
                return true;
            }

            value = default!;
            return false;
        }

        public IEnumerable<string> Keys
        {
            get
            {
                for (var i = 0; i < _count; i++) yield return _keys[i];
            }
        }

        public IEnumerable<T> Values
        {
            get
            {
                for (var i = 0; i < _count; i++) yield return _values[i];
            }
        }

        public IEnumerator<KeyValuePair<string, T>> GetEnumerator()
        {
            for (var i = 0; i < _count; i++) yield return new KeyValuePair<string, T>(_keys[i], _values[i]);
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
