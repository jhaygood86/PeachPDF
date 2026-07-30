# Skip `CascadeApplyStyles`'s per-box defaulting loop for untouched boxes

## The load-bearing idea

A `dotnet-trace` CPU profile of the full 73-showcase `PeachPDF.TestHarness` run showed
`DomParser.CascadeApplyStyles` dominating inclusive time by a wide margin - larger than layout, painting,
and PDF writing combined. Its step 1 ("defaulting", CSS Cascade & Inheritance 4 §2.1) re-asserts every one
of ~131 CSS properties' initial value on **every single box**, via a dictionary dispatch plus (for many
properties) real string validation/parsing work (`IsValidLengthProperty`, `IsValidColorProperty`, etc. all
tokenize the same literal initial-value string every time).

Since the `ComputedStyle`-per-area split (see the same day's other recent-fixes entry), every area's own
`Default` is itself sourced from the same `CssDefaults` store this loop reads from - so re-asserting a
property's initial value on a box whose `ComputedStyle` is still the shared `Default` singleton is now a
guaranteed no-op for almost every property. Verified this exhaustively rather than assuming it: a test
iterates every `CssDefaults.InitialValues` entry, runs it through the exact same `CssUtils.SetPropertyValue`
call the real loop uses against a fresh box, and asserts `ComputedStyle` stays unchanged. Exactly three
properties fail that check - `font-family` (`FontArea.FontFamily`'s own default is a deliberate `null`
sentinel, not the CssDefaults literal font name), and `grid-template-columns`/`grid-template-rows` (their
area default's parsed `GridTemplate` half is a literal `null`, while the real setter parses `"none"` into a
non-null-but-empty `GridTemplate` via `GridTemplateValueConverter.FromCssText`). `CascadeApplyStyles`'s
defaulting loop is now skipped entirely for a box still at `ComputedStyle.Default`, explicitly handling
those three properties instead - any box that has already diverged (e.g. an anonymous box with a
structurally pre-assigned `Display`) still runs the full loop exactly as before, which is what keeps that
loop's existing anonymous-`Display` exception meaningful.

Also removed two now-visibly-redundant `InheritStyle()` calls immediately before a `CascadeApplyStyles`
call (`EnsureListItemMarkers`'s marker box, `SplitFirstLetter`'s first-letter box): `CascadeApplyStyles`
itself unconditionally re-defaults (step 1) and re-inherits (step 2, `box.InheritStyle()`) as soon as it
starts, so these pre-emptive inherits were being discarded and redone every time, for every list-item marker
and every `::first-letter` box in every document.

## Evidence

Full net8.0 suite: 6991 passed / 0 failed / 9 skipped (suite wall time also dropped noticeably, ~37s vs the
~51-114s seen on other runs this session - the same effect showing up in the test suite's own box churn).
Zero-warning `dotnet build PeachPDF.slnx -t:Rebuild`.

Showcase workload (all 73 `PeachPDF.TestHarness` showcases, one full pass), measured against current
`origin/main` (which already includes the single-record `ComputedStyle` refactor, but neither the area
split nor this change) using the same methodology as the area-split benchmark:

| Metric | main | This change | Delta |
|---|---|---|---|
| Wall clock (mean of 3 runs) | 20.19s | 18.23s | **-9.7%** |
| Peak RSS (mean of 3 runs) | 198,193 KB | 190,015 KB | -4.1% |
| Gen0 GC count (one run) | 2,514 | 1,581 | **-37.1%** |
| Gen1 GC count (one run) | 172 | 133 | -22.7% |
| Gen2 GC count (one run) | 30 | 25 | -16.7% |
| Allocated bytes (one run) | ~43.0 GB | ~27.0 GB | **-37.3%** |

This is the real, substantial win the `ComputedStyle` area split's own benchmark write-up (the same day's
other recent-fixes entry) was hoping for and didn't find in either of its two large-document benchmarks -
it just didn't come from the area-reuse mechanism itself. It came from this loop having become fully
redundant as a *side effect* of that mechanism existing (the area split is what made `ComputedStyle.Default`
and `CssDefaults` provably agree on every property), then acting on that once profiling pointed at it
directly, rather than from the area-reuse optimization's own allocation savings on these particular
benchmarked documents.

## Deliberately not done

**Did not change `FontArea.FontFamily`'s default to match `CssDefaults`** (which would have let all three
exceptions collapse into zero) - the `null` sentinel is relied on elsewhere as a "not yet resolved" signal
(`DerivedStyle.ActualFont`'s own lazy fallback), and changing it wasn't verified safe within this pass.
Explicitly handling the three exceptions is a smaller, safer change with identical performance
characteristics (3 dictionary dispatches instead of 131, on the untouched-box fast path).
