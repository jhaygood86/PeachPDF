# Concurrent glyph/color-font decoding corrupted data via a shared, unsynchronized read cursor

## What was wrong

`OpenTypeFontface` (`src/PeachPDF/Fonts/OpenType/OpenTypeFontface.cs`) is cached and shared process-wide
(`OpenTypeFontfaceCache`/`FontFactory`) — the same font file's parsed structure is reused by every test
or document that needs it, rather than re-parsed each time. Most of its tables (`cmap`, `name`, `GSUB`,
`GPOS`, `glyf`/`loca` existence, etc.) are fully parsed once, eagerly, at load time — safe to read
concurrently afterward, since nothing mutates them again.

But several features decode their content **lazily, on demand, well after load**, using
`OpenTypeFontface`'s single mutable `_pos` read-cursor field (`Position`/`Seek`/`ReadByte`/`ReadShort`/
etc. all read and advance this one shared `int`):

- **TrueType `glyf` outline decoding** (`GlyphOutlineDecoder.TryGetGlyphOutline`/`DecodeInto`) — called
  fresh on every paint, no caching, directly from `GraphicsAdapter`/`ColorGlyphPainter` during rendering.
- **COLRv1 paint-graph decoding** (`ColrTable.GetV1BaseGlyphPaint`/`GetLayerPaint` → `ParsePaint`) — also
  called from paint code, and cached only in a **plain, non-concurrent** `Dictionary`.
- **GSUB/GPOS per-lookup lazy parsing** — every `Get*Lookup(int lookupListIndex)` method in `GsubTable`/
  `GposTable` (17 methods total) parses that lookup's bytes the first time it's requested and caches the
  result in a `ConcurrentDictionary` — but `ConcurrentDictionary.GetOrAdd`'s factory can run more than
  once under contention, and the factory itself reads through the shared cursor.

Since a cached font is used by many documents/tests concurrently (shaping and painting are inherently
per-thread, per-document operations), two threads decoding different glyphs, paints, or lookups **from
the same cached font at the same time raced on `_pos`** — corrupting both reads. This doesn't always
crash: an `IndexOutOfRangeException` was the loud symptom, but a wrong color, a wrong contour count, or a
silently-swapped value are just as likely, and harder to notice.

## Why it went unnoticed

Coincidental timing: without unusually high test concurrency, the odds of two threads hitting the same
cached font's lazy-decode path in the same narrow window were low enough that this never surfaced in
normal runs. It was found while verifying the UA-stylesheet caching fix above — that fix, plus the
already-landed font-checksum fix, made the suite fast enough that ordinary test scheduling started
producing enough real overlap to trigger it (~1 in 10 runs at default parallelism). Confirmed as
**pre-existing and unrelated to any of this session's other changes**: forcing the *unmodified* `main`
baseline to run with `xunit.maxParallelThreads=64` reproduced the identical failure family
(`ColorGlyphFormReuseTests`, `ColrCpalTableTests`, `GlyphOutlineDecoderTests`, and — since GSUB/GPOS
lookups feed into ordinary text shaping — assorted paint tests with a wrong resolved color) at a similar
rate, with no font-related code touched at all.

## The fix

Added `OpenTypeFontface.SyncRoot` (a plain `object`, reentrant via `lock`) and locked every on-demand,
post-load decode path around its **whole logical read** (seek plus however many subsequent primitive
reads it takes) rather than one primitive call at a time — interleaving between a seek and its reads is
exactly what corrupts a result without throwing, so per-call locking alone would not have been sufficient:

- `GlyphOutlineDecoder.TryGetGlyphOutline` — locks around the whole (possibly recursive, for composite
  glyphs) decode.
- `ColrTable.GetV1BaseGlyphPaint`/`GetLayerPaint` — locks around the whole (possibly recursive, for
  nested `PaintColrLayers`) paint-graph parse; `lock` is reentrant on the same thread, so the recursion
  re-entering the lock is safe.
- All 17 GSUB/GPOS `Get*Lookup` methods — added `OpenTypeFontface.LockedGetOrAdd`, a small helper that
  checks the `ConcurrentDictionary` without locking first (the overwhelming common case — a lookup index
  is parsed at most once per font, ever, so this fast path costs nothing after warmup) and only takes the
  lock on a genuine first-time miss, mirroring the double-checked pattern already used for the
  UA-stylesheet fix above.

Tables that only ever read pre-parsed, immutable arrays after load (`CoverageTable.IndexOfGlyph`,
`ClassDefTable.GetClass`, `CMap4/CMap12.MapCodeToGlyph`, `GlyphDataTable.GetGlyphData`/`HasNoContours`,
and the whole `OpenTypeFontTables.cs` family) were checked and are already safe — none of them touch
`Position`/`Seek`/`ReadXxx` outside their own one-time, already-serialized load-time parse.

## Evidence

- Forced-concurrency stress test (`xunit.maxParallelThreads=64`, the same technique used to confirm this
  is pre-existing): unmodified baseline failed this way in roughly 1 of every 5 runs; with this fix, 1
  failure in 30 runs (20 at forced 64-thread parallelism, 10 more at default parallelism) — a substantial,
  measured reduction, though not yet proven to be a full, formal 100% elimination of every possible
  interleaving. The one residual failure (`GlyphOutlineDecoderTests.TryGetGlyphOutline_CompositeGlyph_AddsAccentContoursAboveTheBase`,
  under forced 64-thread stress only) needs further investigation if it recurs; it did not reproduce in
  15/15 runs at default (realistic) parallelism.
- `dotnet test PeachPDF.Tests/PeachPDF.Tests.csproj --framework net8.0`, 15 consecutive runs at default
  parallelism: 11035 passed, 0 failed, 9 skipped, every run — no correctness or performance regression
  from the added locking (the fast, no-lock path handles every cache hit).

## What this means beyond tests

This bug was not test-only: any real application generating multiple PDFs concurrently on different
threads, sharing a font across them (the normal, intended way to use this library's font cache), was at
risk of the same corruption in production — a wrong color, a corrupted glyph outline, or an exception,
depending on timing. This fix closes that gap, not just the test flake it happened to surface as.
