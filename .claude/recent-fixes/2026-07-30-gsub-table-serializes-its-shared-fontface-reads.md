# GsubTable serializes its shared-fontface reads

Fixes [#543](https://github.com/jhaygood86/PeachPDF/issues/543): the CLI test suite crashing on
`windows-latest` CI, regressed by `#536`'s GSUB ligature support.

## The load-bearing idea

`OpenTypeFontface` (`Fonts/OpenType/OpenTypeFontface.cs`) is cached and shared process-wide by
`OpenTypeFontfaceCache` (keyed by checksum, then by name) - a `PdfGenerator` never gets its own private
copy of a font it has already seen. Its `Position` property is a plain mutable `int` field with no
synchronization at all, and every table reader in `Fonts/OpenType/` uses the same "seek, then read
several values in sequence" pattern against it. `GsubTable.GetActiveLookupIndices` (added by `#536`)
re-reads the ScriptList/FeatureList tables through that pattern on **every single** `Shape()` call -
unlike `GetLigatureLookup`, its result isn't cached - making it by far the widest, most frequently hit
critical section touching the shared cursor. Two threads shaping text concurrently against the same
cached font (two overlapping renders, or two xUnit test classes under the default parallel-collection
runner) interleave their `Position` writes/reads, corrupting what each thread reads back. `GsubTable`'s
own methods (`GetActiveLookupIndices`, `ReadLigatureLookup` and the two private helpers only ever
reached through it) now `lock (_face)` around their sequential-read sections, closing that race for the
newly-added GSUB path.

## What was found by running it, not by reading it

**Confirmed the race is real and the fix closes it, not just plausible.** Temporarily replacing both
`lock (_face)` blocks with a no-op made the new `ConcurrentAccess_FromManyThreads_ProducesConsistentResults`
test (in `GsubTableSyntheticTests.cs`, which hammers one shared synthetic `GsubTable` from many threads
via `Parallel.ForEach`) fail reliably - 5/5 runs, either a wrong-result assertion or a thrown exception,
matching the shape of the CI crash exactly. Restoring the lock made it pass consistently across repeated
runs. This is the same class of hazard CLAUDE.md already documents for `FontFactory`/OpenType caching
generally (parallel-test-class flakiness), but this is the first fix rather than just a written warning
to avoid triggering it.

**Confirmed via CI archaeology, not guesswork, that the regression window starts at `#536`.** Walking
`main`'s own commit-by-commit CI history (`fe4ad52` and `afac876` both fail on `windows-latest`;
`b2ff99c`, the commit immediately before `#536` merged, is green) pinned the introduction precisely
before any fix was attempted, ruling out font-metrics/monolithic-content/unpaginated-mode hypotheses that
were considered first.

## What was deliberately not done

- **The same unsynchronized-shared-cursor hazard almost certainly also exists for glyph outline
  decoding** (`GlyphOutlineDecoder.cs`/`GlyphDataTable.cs`/`IndexToLocationTable.cs`) and other table
  readers (`ColrTable.cs`, `CpalTable.cs`) that read through the same shared `OpenTypeFontface.Position`
  - none of them take any lock either. This wasn't touched here: those paths pre-date `#536` and were not
    the regression `#543` reported (CI was green through them for the entire history checked), and fixing
    the whole subsystem's concurrency model is a materially larger change than the "small, isolated fix"
    this was scoped as. The likely reason `#536` is what tipped CI over rather than these older paths:
    `GetActiveLookupIndices` runs its full multi-table read on every `Shape()` call with no caching, while
    outline decoding and lookup caching mostly memoize after a first hit - a much longer, more frequently
    re-entered critical section is a much bigger collision target under concurrent load.
- **No lock was added to `OpenTypeFontface` itself.** Locking on `_face` from within `GsubTable`'s own
  methods is sufficient to serialize GSUB-vs-GSUB access to a shared face; it does not protect against a
  concurrent glyph-outline read on the same face interleaving with a GSUB read, since that reader takes no
  lock. Closing that fully would mean auditing and locking every `OpenTypeFontface` reader, which is out of
  scope for this fix - noted here for whoever eventually hardens that subsystem.

## Evidence

Full `net8.0` suite green (7081/7081, was 7080 pre-fix + 1 new concurrency test). Zero-warning solution
rebuild. 100% diff coverage against `origin/main` (68/68 lines - one branch, an empty `ScriptList`, needed
a dedicated synthetic-font test to close after the lock wrapping shifted line numbers). Full 77-showcase
corpus byte-identical before/after (expected - this only adds synchronization around unchanged single-
threaded logic). New `ConcurrentAccess_FromManyThreads_ProducesConsistentResults` and
`EmptyScriptList_ReturnsEmpty` tests in `GsubTableSyntheticTests.cs`.
