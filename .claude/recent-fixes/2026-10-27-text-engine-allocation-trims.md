# PeachDrawing.Text allocation trims (Batch C)

Pure allocation/CPU cleanups in the text engine; output is unchanged.

- `FontResolver`: `InstalledFonts`/`_systemFamilies` use `StringComparer.OrdinalIgnoreCase`, so lookups no longer `ToLowerInvariant()` the name. Stored keys stay lowercase (`FindFamilyCoveringCodepoint` returns them and sorts them ordinally). `HasExplicitRanges` is cached per family in `_hasExplicitRangesCache`; `AddFont` drops that family's entry when it replaces the family model — a stale `false` after adding a ranged face is the trap.
- `HyphenationEngine`: wrapped word and gap table are stack buffers (heap beyond 256); net9+ looks patterns up through `GetAlternateLookup<ReadOnlySpan<char>>`, net8 keeps a `new string(span)` probe.
- `Paragraph`: script-set membership is answered once per run of equal script instances (reference compare, falling back to recompute when the instance differs); `ShapePiece` builds only the one per-codepoint list it will use.
- `GlyphOutlineDecoder`: gvar's throwaway original-coordinate arrays are rented from `ArrayPool` (cleared first - phantom points must read 0). `flags`/`xs`/`ys` are NOT pooled: they escape through `out` parameters shared with the bounds path.
- `OpenTypeFontTable.CalcChecksum` sums big-endian words via `MemoryMarshal.Cast`; `OpenTypeFontface` readers and `Woff2Converter` use `BinaryPrimitives`; `ReadTag` decodes Latin-1 directly; `Seek` does one `TryGetValue`.

Trap: `BinaryPrimitives` over `AsSpan(pos)` throws `ArgumentOutOfRangeException` where the old indexer threw `IndexOutOfRangeException`. Every existing catch site lists both or `ArgumentException`; keep it that way.

Evidence: `AllocationReductionEquivalenceTests` (checksum vs the old lane sum, case-insensitivity, the 256-char stack/heap boundary, cache invalidation); Text.Tests 6583 pass on net8 and net10 (Release), PeachPDF.Tests net8 Release pass, zero-warning solution Rebuild.
