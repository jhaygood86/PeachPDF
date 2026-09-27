# A hinting cache hit takes no lock, and a size is made once

Measured with `PeachDrawing.Text.Benchmarks` (baseline in [the benchmarks entry](2026-09-26-hinting-benchmarks-and-baseline.md)), before and after on the same
machine in the same session, timings indicative (the machine was at 90-99% CPU from other work), results identical.

| Hot, ns per lookup per thread | 1 thread | 4 threads | 8 threads |
| --- | --- | --- | --- |
| LiberationSans, before | 52 | 570 | 1,252 |
| LiberationSans, after | 27-29 | 16-35 | 31-51 |
| HintingCff, before | 48 | 800 | 1,451 |
| HintingCff, after | 25-26 | 34-43 | 43-59 |

The cold benchmark (every glyph of a font into an empty cache) is unchanged in time and in allocation (the front cache below is made by the first hit, so a load
that never hits does not have it).

## What was slow, and what was not

- **The lock was the whole cost, and the reordering inside it was not.** Every hit took the cache's one lock and moved the entry to the front of a linked list, a
  write to memory every thread shares: with eight threads asking for the same few hundred glyphs each lookup cost 20-50 times what it costs alone. Skipping the
  reorder when the entry is already first (the first thing tried, the obvious one) moved the 8-thread figure from ~1,450 to ~1,070-1,270 ns: within the noise of
  what a lock convoy costs. A lock-free read path was the only thing that mattered.
- **The read path is a direct-mapped array of immutable entries in `HintingEngine`** (`_front`, 2048 slots, made by the first glyph found in the LRU). A hit is a
  slot read and a key compare. A thread that finds nothing asks the LRU and stores what it finds; an entry that leaves the LRU (evicted, or replaced by `Set`) is taken
  out of the array from the LRU's `removed` callback, so a glyph that was evicted is hinted again, as `HintingEngineTests` asserts (the new test asks for the glyph
  again and again first, so that it is in the array when it is evicted; with the removal disabled it fails).
- **What it costs, decided on.** A hit in the array does not move the entry to the front of the LRU, so a glyph asked for over and over, only ever served from the
  array, can reach the end of the LRU and go out of it; the next ask hints it again (one more miss, never a different answer: an entry is a pure function of its key).
  This only matters when the cache is full (4096 glyphs or 250,000 weight per face). `LruCache` itself stays strictly least-recently-used: it is not where the front
  is, and a randomized comparison against a list kept in order of use (`LruCacheTests`) covers Set/TryGet/GetOrAdd, weights and evictions, entry by entry. The
  removal that a racing thread can lose (a thread stores an entry for a key the LRU has just evicted) leaves a stale entry until its slot is overwritten: it is bounded
  by the array, and it is the same outline.

## The rest of the change

- `LruCache` is its own intrusive list now: no `LinkedListNode<KeyValuePair<,>>`, an evicted node is reused for the entry that comes in its place, and an entry's
  weight is worked out once, outside the lock, and kept on the node (eviction used to weigh every victim again under the lock, walking its outline).
- **Single-flight `GetOrAdd`** for the two size caches: threads asking for a size that is not there at the same time used to each run the font's `fpgm` and `prep`
  (`TtSize.Create`, the expensive part of a size); one does now and the others wait for it. If the maker throws, the exception goes to it alone, nothing is kept, and a
  waiter makes the value itself. Glyphs are not single-flighted: hinting a glyph twice costs less than the bookkeeping would.
- The size caches still remember a failure (`null`): the factory returns it and `Set` keeps it.

## Not done

A thread-local front cache (not tried): a `[ThreadStatic]` one would pin the outlines (and their engine, and its font's bytes) of every font a thread ever used, and one
per engine needs a `ThreadLocal` and a way to reach every thread's copy to invalidate it. Sharding the lock (a strict LRU per shard is not a strict LRU). Approximating LRU
with a CLOCK bit (the tests assert exact order). Batching the reorders per thread and applying them before any eviction (keeps strict order, but a lot more machinery than
an array read; the measured cost of the array already leaves a hit at the cost of the code around the cache).
