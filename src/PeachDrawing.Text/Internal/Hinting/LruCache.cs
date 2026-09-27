using System;
using System.Collections.Generic;
using System.Threading;

namespace PeachDrawing.Text.Internal.Hinting;

/// <summary>A thread-safe cache that keeps the most recently used entries, up to a number of them and, if asked, a total weight.</summary>
internal sealed class LruCache<TKey, TValue>
    where TKey : notnull
{
    private readonly int _capacity;
    private readonly Func<TValue, int>? _weigher;
    private readonly long _maxWeight;
    private readonly Action<TKey, TValue>? _removed;
    private long _weight;
    private readonly Dictionary<TKey, Node> _map = [];
    private readonly Dictionary<TKey, Making> _making = [];
    private Node? _first; // the most recently used
    private Node? _last; // the least recently used
    private readonly object _lock = new();

    /// <param name="capacity">The most entries kept.</param>
    /// <param name="weigher">What an entry weighs, or null when entries are not weighed. It is called once for each value, outside the lock.</param>
    /// <param name="maxWeight">The most weight kept: the newest entry is always kept, however heavy, and the older ones go first.</param>
    /// <param name="removed">
    /// Told of each entry that leaves the cache, evicted or replaced, and only of those. It runs under the cache's lock: it must be quick and must not call back
    /// into the cache. It is for whoever keeps a copy of the entries elsewhere and has to forget them along with the cache.
    /// </param>
    public LruCache(int capacity, Func<TValue, int>? weigher = null, long maxWeight = long.MaxValue, Action<TKey, TValue>? removed = null)
    {
        _capacity = capacity;
        _weigher = weigher;
        _maxWeight = maxWeight;
        _removed = removed;
    }

    public bool TryGet(TKey key, out TValue? value)
    {
        lock (_lock)
        {
            if (_map.TryGetValue(key, out Node? node))
            {
                // the entry that was used last is where it belongs already, which is nearly always so for a glyph that is asked for again and again
                if (!ReferenceEquals(node, _first))
                {
                    Unlink(node);
                    LinkFirst(node);
                }

                value = node.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    /// <summary>
    /// The value of a key, made by <paramref name="create"/> and kept when the cache has none. When several threads ask for the same key at the same time, one
    /// runs <paramref name="create"/> and the others wait for it and take what it made, so that an expensive value is made once and not once per thread.
    /// </summary>
    /// <remarks>
    /// If <paramref name="create"/> throws, the exception goes to the thread that ran it, nothing is kept, and a thread that waited for it tries again itself.
    /// <paramref name="create"/> runs outside the lock and may take any time, but it must not ask this cache for the same key.
    /// </remarks>
    public TValue GetOrAdd(TKey key, Func<TKey, TValue> create)
    {
        while (true)
        {
            Making? making;
            bool ours = false;

            lock (_lock)
            {
                if (_map.TryGetValue(key, out Node? node))
                {
                    if (!ReferenceEquals(node, _first))
                    {
                        Unlink(node);
                        LinkFirst(node);
                    }

                    return node.Value;
                }

                if (!_making.TryGetValue(key, out making))
                {
                    making = new Making();
                    _making[key] = making;
                    ours = true;
                }
            }

            if (!ours)
            {
                making.WaitUntilDone();
                continue; // what it made is in the map now, unless it failed or has already been pushed out: then this thread makes it
            }

            try
            {
                TValue value = create(key);
                Set(key, value);
                return value;
            }
            finally
            {
                lock (_lock)
                    _making.Remove(key);

                making.Done();
            }
        }
    }

    public void Set(TKey key, TValue value)
    {
        // what the value weighs is worked out once, and not under the lock that every reader of the cache waits for
        int weight = _weigher?.Invoke(value) ?? 0;

        lock (_lock)
        {
            Node node;
            if (_map.TryGetValue(key, out Node? existing))
            {
                // the entry keeps its place in the map and takes the value and the weight of the new one
                node = existing;
                Unlink(node);
                _weight -= node.Weight;
                _removed?.Invoke(node.Key, node.Value);
            }
            else if (_map.Count >= _capacity && _last is { } oldest)
            {
                // the cache is full: the entry that goes is the one that comes, in its place
                node = oldest;
                Unlink(node);
                _map.Remove(node.Key);
                _weight -= node.Weight;
                _removed?.Invoke(node.Key, node.Value);
                node.Key = key;
                _map[key] = node;
            }
            else
            {
                node = new Node { Key = key };
                _map[key] = node;
            }

            node.Value = value;
            node.Weight = weight;
            _weight += weight;
            LinkFirst(node);

            while (_map.Count > _capacity || (_weight > _maxWeight && _map.Count > 1))
            {
                Node last = _last!;
                Unlink(last);
                _map.Remove(last.Key);
                _weight -= last.Weight;
                _removed?.Invoke(last.Key, last.Value);
            }
        }
    }

    private void Unlink(Node node)
    {
        if (node.Previous is { } previous)
            previous.Next = node.Next;
        else
            _first = node.Next;

        if (node.Next is { } next)
            next.Previous = node.Previous;
        else
            _last = node.Previous;

        node.Previous = null;
        node.Next = null;
    }

    private void LinkFirst(Node node)
    {
        node.Next = _first;
        node.Previous = null;

        if (_first is { } first)
            first.Previous = node;
        else
            _last = node;

        _first = node;
    }

    /// <summary>A value that a thread is making, which the threads that want it wait for.</summary>
    private sealed class Making
    {
        private bool _done;

        public void WaitUntilDone()
        {
            lock (this)
            {
                while (!_done)
                    Monitor.Wait(this);
            }
        }

        public void Done()
        {
            lock (this)
            {
                _done = true;
                Monitor.PulseAll(this);
            }
        }
    }

    private sealed class Node
    {
        public TKey Key = default!;
        public TValue Value = default!;
        public int Weight;
        public Node? Previous;
        public Node? Next;
    }
}
