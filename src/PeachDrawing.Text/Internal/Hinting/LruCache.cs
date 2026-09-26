using System;
using System.Collections.Generic;

namespace PeachDrawing.Text.Internal.Hinting;

/// <summary>A thread-safe cache that keeps the most recently used entries, up to a number of them and, if asked, a total weight.</summary>
internal sealed class LruCache<TKey, TValue>
    where TKey : notnull
{
    private readonly int _capacity;
    private readonly Func<TValue, int>? _weigher;
    private readonly long _maxWeight;
    private long _weight;
    private readonly Dictionary<TKey, LinkedListNode<KeyValuePair<TKey, TValue>>> _map = [];
    private readonly LinkedList<KeyValuePair<TKey, TValue>> _order = new();
    private readonly object _lock = new();

    /// <param name="capacity">The most entries kept.</param>
    /// <param name="weigher">What an entry weighs, or null when entries are not weighed.</param>
    /// <param name="maxWeight">The most weight kept: the newest entry is always kept, however heavy, and the older ones go first.</param>
    public LruCache(int capacity, Func<TValue, int>? weigher = null, long maxWeight = long.MaxValue)
    {
        _capacity = capacity;
        _weigher = weigher;
        _maxWeight = maxWeight;
    }

    public bool TryGet(TKey key, out TValue? value)
    {
        lock (_lock)
        {
            if (_map.TryGetValue(key, out var node))
            {
                _order.Remove(node);
                _order.AddFirst(node);
                value = node.Value.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    public void Set(TKey key, TValue value)
    {
        lock (_lock)
        {
            if (_map.TryGetValue(key, out var existing))
            {
                _order.Remove(existing);
                _map.Remove(key);
                _weight -= _weigher?.Invoke(existing.Value.Value) ?? 0;
            }

            var node = new LinkedListNode<KeyValuePair<TKey, TValue>>(new KeyValuePair<TKey, TValue>(key, value));
            _order.AddFirst(node);
            _map[key] = node;
            _weight += _weigher?.Invoke(value) ?? 0;

            while (_map.Count > _capacity || (_weight > _maxWeight && _map.Count > 1))
            {
                LinkedListNode<KeyValuePair<TKey, TValue>> last = _order.Last!;
                _order.RemoveLast();
                _map.Remove(last.Value.Key);
                _weight -= _weigher?.Invoke(last.Value.Value) ?? 0;
            }
        }
    }
}
