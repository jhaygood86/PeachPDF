# The UA default stylesheet was re-parsed from scratch on every test

## What was wrong

`RAdapter._defaultCssData` (`src/PeachPDF/Html/Adapters/RAdapter.cs`) was an **instance field**, lazily
parsed once per adapter: `if (_defaultCssData is null) _defaultCssData = await CssData.Parse(this,
CssDefaults.DefaultStyleSheet, false);`. `CssDefaults.DefaultStyleSheet` is a fixed, 11.5KB, 212-line
constant — identical for every adapter, every document. But `RAdapter`/`PdfSharpAdapter` instances are
constructed fresh per test by design (the same pattern behind
[2026-09-12-pdfsharpadapter-reuses-cached-system-font-names.md](2026-09-12-pdfsharpadapter-reuses-cached-system-font-names.md)),
so every test's first `SetHtml` re-tokenized and re-parsed that same 11.5KB from scratch, even though the
result never changes.

This class's own doc comment (`"It is best to have a singleton instance of this class... because it
holds caches of default CssData, Images, Fonts and Brushes"`) describes the ORIGINAL intended usage
pattern — a real singleton `RAdapter` — which this codebase has already deliberately diverged from for
performance (fresh instances per document/test), fixing the resulting per-instance-cache mismatch one
cache at a time as each is found to matter (system font names, font checksums, and now this).

## The fix

`_defaultCssData` is now a `static Task<CssData>?` with a thread-safe double-checked lock (tests run
with default parallelism, so this must be safe under concurrent first access), computed once for the
whole process regardless of which adapter instance triggers it. Safe to share: `CssData.Clone()` is a
shallow copy, and `DomParser.CloneCssData` already clones-before-mutate the first time any document adds
its own `<style>`/`<link>` rules on top of the returned `CssData` — so nothing ever mutates the shared
instance's `Stylesheets` list in place; a document with author styles gets its own copy at the point it
actually needs one, exactly as it already did when `_defaultCssData` was per-instance.

## Evidence

Verified via the full suite (`dotnet test PeachPDF.Tests/PeachPDF.Tests.csproj --framework net8.0`):
11035 passed, 0 failed, 9 skipped — no behavior change, confirming the shared-instance safety reasoning
above holds in practice, not just on paper. Measured contribution to the coverage-collecting CI-style run
(combined with the container-relative-unit fix below): coverage-collecting full-suite duration dropped
from 56s to ~50s on top of the font-checksum fix already landed — a real but more modest improvement than
the raw CSS-lexer coverage hit-count reduction alone would suggest (coverage instrumentation overhead
isn't purely linear in line-hit count), so it's reported here as measured, not estimated from the hit
counts.
