# A method group passed to GetOrAdd allocates per call

`GposTable` and `GsubTable` cache every parsed lookup —
`_cache.GetOrAdd(lookupListIndex, ReadSomething)`. The second argument is a **method group**, and
converting one to a `Func<>` allocates a fresh delegate on *every call*, including the
overwhelmingly common one where the cache already holds the value and the factory is never
invoked. The positioner and the shaper ask these questions once per glyph per lookup, so the
delegate was allocated per glyph while the thing it existed to build was allocated once.

Seventeen getters across the two tables now hold the delegate in a field (`??=` on first use, so no
constructor changes).

## What measuring it turned up

- **Worth 57 MB per corpus pass, 7.7% of the total** — 734.6 MB down to 677.9 MB over 26 real
  documents. Per-word allocation on a text-heavy document drops from **12.26 KB to 9.76 KB**, a
  fifth of the shaping cost.
- **It is exactly 64 bytes a call** — 6,000 cache hits allocated 384,000 bytes before, and zero
  after. That is one delegate, which is what makes this testable to the byte rather than as a
  percentage of a render.
- **The profile pointed straight at it.** `GC/AllocationTick` with call stacks put
  `System.Func<int,int>` and `System.Func<int,GposMarkToBaseLookup>` in the top ten allocated types
  of a whole document render, which is not a shape anyone writes on purpose — a `Func` appearing
  high in an allocation profile of a *layout engine* is the tell.
- **Nineteen such sites exist**; the two remaining ones (`HyphenationEngine`, `MimeTypeResolver`)
  are not on a per-glyph path and are left alone.

## Evidence

`GposTableSyntheticTests.ALookupCacheHit_AllocatesNothing` primes the caches against a synthetic
face and then asserts 6,000 hits allocate under 4 KB — 384,000 bytes against `main`. Asserted at the
table rather than through a rendered document because allocation over a document scales with
whatever font a machine resolves; with the cache primed, the delegate is the only thing left that
can allocate. Per-thread, because the suite runs collections in parallel.

Full suite green on net8.0 (10,423), 0 new build warnings, corpus fidelity unchanged at 26/26.
