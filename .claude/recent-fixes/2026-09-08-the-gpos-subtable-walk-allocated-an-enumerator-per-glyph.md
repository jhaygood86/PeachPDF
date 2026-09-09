# The GPOS subtable walk allocated an enumerator per glyph

Every lookup's `Subtables` is declared `IReadOnlyList<T>`, so a `foreach` over it boxes an enumerator
through the interface. `GposPositioner` does that once per glyph — `ApplyMarkToBase` calls straight
into the loop for every glyph in the run — so the cost lands on every glyph of every text run,
whether or not the font has anything to position. Eight such loops are now indexed instead.

## What measuring it turned up

- **Worth 85 MB per corpus pass**, 9% of everything upstream `main` allocates over 26 real
  documents (930.16 MB down to 844.75 MB). Additive with the anonymous-box defaulting fix: the two
  together take it to 733.25 MB.
- **It fires on plain Latin text**, where there is nothing to attach at all. The enumerator is
  allocated before any coverage check, so a document with no combining marks pays it in full.
- **The first version of the test did not reproduce it**, and the reason is worth knowing:
  `List<T>` hands back a *cached singleton* enumerator when it is empty, so a lookup with an empty
  `Subtables` allocates nothing and the test passed against the unfixed code. It needs at least one
  subtable — one whose coverage matches nothing keeps the loop body cold while still walking.
- **`GC.GetTotalAllocatedBytes` is useless here.** It is process-wide, and this suite runs
  collections in parallel, so the first passing run of the whole suite reported 1.2 MB of other
  tests' allocation. `GC.GetAllocatedBytesForCurrentThread` is exact: 400,000 bytes against the
  unfixed code, 40 per visit over 10,000 visits, and zero with the fix.
- **One `foreach` was left alone.** `ApplyMatchedLookups` walks a `GposSequenceLookupRecord[]`, and
  an array's enumerator is a struct that allocates nothing — converting it would have been noise.

## Evidence

`GposPositionerAllocationTests` calls `ApplyMarkToBase` directly against a lookup whose one subtable
covers no glyph, so the only thing that can allocate is the walk. Asserted at the positioner rather
than through a rendered document deliberately: allocation over a document scales with whatever font
a machine resolves, so any end-to-end threshold is loose on one platform and wrong on another.

Full suite green on net8.0 (10,388), 0 new build warnings.
