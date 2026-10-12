# A word is shaped once, not four times

## The load-bearing idea

Allocation profile of a 131-page prose document (EventPipe allocation sampling): 30.5% of everything allocated was under
`Shaper.Shape`, and the call pattern explains why. The layout engine measures a word to place it (`MeasureString` ->
`TextMeasurement.Measure` -> `Shaper.Shape`); then the painter, for the same word, measures it again to align it, calls
`CharacterMapInfo.AddShapedText` to build the font's ToUnicode map, and calls `Shaper.Shape` once more for the glyphs it
writes. **Four shapes of identical input**, three of them back to back.

`Shaper.Shape` now serves a word it has already shaped from a per-`Typeface` cache of `GlyphRun`s
(`Internal/Text/ShapedRunCache.cs`). Sharing is safe because shaping is a pure function of face, text and settings and the
result is immutable: `GlyphRun` has only getters, `PlacedGlyph` is a readonly record struct, and only `Shaper.Shape` reaches
`OpenTypeDescriptor.Shape`, so nothing can mutate a shaped list.

## What the cache does and does not hold

- **Keyed by (text, `ShapeSettings`) on the `Typeface` itself**, so a variable-font instance or another face never shares an entry
  and the entries go away with the face.
- **Only settings without per-call collections are kept.** Arabic joining forms, USE/Khmer categories and explicit
  `font-feature-settings` are lists a caller builds per run and `ShapeSettings` compares by reference, so a run shaped with them
  could never hit; keeping them would only fill the cache with unfindable keys. They are shaped every time, as before.
- **Bounded by count (16,384 runs per face) and emptied whole when full.** A document's working set of words is small next to the
  bound, so starting over costs a few misses, and no recency bookkeeping sits on the path every word takes.
- A hit allocates nothing (asserted).

## Other allocation removed on the same path

- **`GsubShaper`/`GposPositioner` cached their active lookup sets as `SortedDictionary`/`SortedSet` and enumerated them per
  shaped word.** Enumerating a sorted collection allocates its enumerator and the node stack behind it; that was ~10% of
  allocation (`Node[KeyValuePair<int,int>][]`, `Stack<SortedSet.Node>`). They are arrays now. The `GetOrAdd` factories also
  captured the table, i.e. a closure and a delegate per call even on a hit; they are `static` lambdas taking the table as an
  argument. 952 -> 812 MB on the 131-page document by themselves.
- **`TextMeasurement.Measure` took a `Rune.ToString()` per character, a `StringBuilder`, a closure for its local function and a
  boxed glyph enumerator, for every word.** A plain run (no control character, no surrogate) is shaped as given with none of
  that; the general line-by-line path moved to its own method so its closure is only paid for when needed. Behaviour is pinned
  by new `TextMeasurementTests` that pass on both the old and the new implementation.
- **`CharacterMapInfo.AddShapedText` cut a `Substring` per glyph of every drawn word** to compare against the mapping it already
  held. It now compares in place and only builds the string when the mapping changes. The three `foreach` over
  `IReadOnlyList<PlacedGlyph>` on the draw path (boxed enumerators, 4.3%) are indexed loops.

## What was found by running it

- The synthetic prose document has 19 distinct words, so its hit rate flatters any cache. The numbers below are also taken on a
  document built from the repository's own prose (89,559 words, 7,566 distinct, ~8% unique): the same allocation and nearly the
  same time, which is the figure to believe.
- `FragmentEmitter` is not in this profile any more (it was 65% of a 1,315-page document before the unreached-boxes fix).

## Evidence

- In-process, warm, per render (this branch on `main` + the unreached-boxes fix): 131-page prose 1.24s / 952 MB -> 0.79s / 522 MB;
  realistic-vocabulary prose 1.41s / 944 MB -> 0.84s / 523 MB; 1,315-page prose 16.3s / 10.0 GB -> 12.1s / 5.7 GB;
  80-page table 1,263 -> 1,187 MB; 100-page invoices 483 -> 444 MB.
- Output identical to the build without it: same words (which includes the ToUnicode map, since extraction reads it) and drawing
  operations on every page for 22 documents - prose (synthetic and realistic), table, invoices, two-column and its variants,
  math, images, small documents and the widows/orphans sweeps.
- `ShapedRunCacheTests` (same run for same face/text/settings; never confused across kerning on/off, faces or text; uncacheable
  settings neither shared nor kept; a full cache starts over and still shapes correctly; four threads get the right glyphs; a hit
  allocates nothing), `TextMeasurementTests` (16 cases on both paths).
- Full suites green on net8.0 in Release: text engine 6,570, Core 152, rasterizer 228, PeachPDF 16,430. Whole solution
  `-t:Rebuild` has 0 warnings.

## What this did not do

Shaping of a word that has not been seen is as expensive as before; only repeats are free. The per-word `String`/`Char[]`
allocation in `DrawString` (building the Tj literal) and in `CssBox` word creation remain.
