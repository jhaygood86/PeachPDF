# A property getter that ran a LINQ query per read

`StyleRule.Selector` and `StyleRule.Style` were both
`Children.OfType<T>().FirstOrDefault()`. `StylesheetNode.Children` hands the child list back as
`IEnumerable<IStylesheetNode>`, so each read built an `OfType` iterator and boxed a list
enumerator — two allocations, every time a property was read.

Selector matching reads `Selector` once per box per candidate rule, so a document with many rules
and many boxes pays it per pair. `ChildList`, a new `protected IReadOnlyList<IStylesheetNode>` on
the node, lets both getters walk the children by index instead.

## What measuring it turned up

- **40 MB per corpus pass, 6.5% of the total** — 619.3 MB down to 579.3 MB over 26 real documents,
  measured against `main` *after* #977 and #978 landed. It was 39 MB before those, so #978's rewrite
  of selector matching did not absorb it: the getter is read from more places than that path.
- **88 bytes per read, exactly**: 4,000 property reads allocated 352,000 bytes before and zero
  after. Two allocations at 44 bytes apiece, which is what makes this testable to the byte.
- **A property getter in an allocation profile is the tell.** `StyleRule.get_Selector` came fourth
  in the ranked call sites of a whole document render, above the fragment emitter. Nobody expects a
  property read to be a top-five allocator, which is exactly why it survived.
- **The setter still reads the getter** (`ReplaceSingle(Selector, value)`), so the write path is
  unchanged and there is no cached state to invalidate — the reason this is an indexed walk rather
  than a memoised field.

## Evidence

`StyleRuleLookupAllocationTests` asserts 4,000 reads allocate under 4 KB, and separately that the
indexed walk still finds the same children the LINQ form did — including after `SelectorText` has
replaced the selector, which is the one case where the child is not the one the constructor added.
Per-thread, because the suite runs collections in parallel.

Full suite green on net8.0 (10,424), 0 new build warnings, corpus fidelity unchanged at 26/26.
