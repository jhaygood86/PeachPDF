# Cost-ordered, zero-allocation selector matching

`CssData.DoesSelectorMatch`'s `ListSelector`/`CompoundSelector` overloads called LINQ's
`Any()`/`All()`/`Last()` over `Selectors`, which only implements `IEnumerable<ISelector>` (not
`IList<ISelector>`) — so each call boxed an enumerator through the interface and allocated a closure,
on the hottest path in the whole cascade (once per box per candidate rule). `Selectors` already
exposes `Length` and an indexer, so the same logic is now an indexed loop with no interface dispatch
and no closure. Reported (with a full GC profile and a repro attempt) in [issue
#971](https://github.com/jhaygood86/PeachPDF/issues/971), whose author couldn't land a PR because
every test they tried to isolate the effect gave no signal — see "What testing it turned up" below.

On top of the allocation fix, `Selectors` gained a cached `MatchOrder`: an `int[]` of indices into
`_selectors`, sorted once (lazily, on first use) by a new `SelectorMatchCost.Of(ISelector)` ranking —
cheap checks (`IdSelector`/`ClassSelector`/`TypeSelector`) first, expensive ones (`HasSelector`,
`NotSelector`/`MatchesSelector`, `LangSelector`, structural `ChildSelector`/`OnlyChildSelector`/
`OnlyOfTypeSelector`) last. `DoesSelectorMatch`'s `All`/`Any` loops iterate in that order instead of
source order, so a cheap conjunct/alternative short-circuits before an expensive one ever runs — the
same technique real selector engines (e.g. WebKit's `SelectorChecker`) use. `MatchOrder` is a separate
view layered on top of `_selectors`; it does not reorder `_selectors` itself, so `ToCss()`/`Text`,
`Specificity`, and the CSS-grammar invariant that a trailing `PseudoElementSelector` is structurally
last (found via `compoundSelector[compoundSelector.Length - 1]`, never via `MatchOrder`) all still see
source order exactly as before.

The same `Where()`/`Any()`/`Last()`-over-`Selectors` shape existed at four more call sites in the same
file, some genuinely adjacent to the ones the issue measured — `GetMatchedSpecificity` (once per
matched rule per box, on the same `ThenBy` sort as `DoesSelectorMatch`), `MatchesAsFirstLineSelector`/
`HasFirstLineSubject` (per box, for `::first-line` rules), `CouldMatchAsFirstLineSelector` (once per
rule at index-build time - never actually hot, fixed anyway for consistency), and `HasRelativeMatch`
(`:has()`'s comma-separated relative-selector-list argument). All four are now indexed too, reusing
`MatchOrder` where the call is a pure OR/AND (safe to reorder) and a plain loop where every alternative
has to be examined regardless (`GetMatchedSpecificity` needs the *maximum* matched specificity, not
just the first match, so cost-ordering doesn't help there — only the allocation removal does).

**Left alone on purpose:** `ComplexSelector.LastOrDefault()` (`MatchesAsFirstLineSelector`,
`CouldMatchAsFirstLineSelector`) has the identical allocation shape, but `ComplexSelector` is a
different, unrelated type with no indexer of its own (only `IEnumerable<CombinatorSelector>` and a
`Length` property) — fixing it means adding new API surface to a second class, not reusing the
`Selectors`/`MatchOrder` infrastructure this PR built. Deferred as a separate, smaller follow-up rather
than folded in here.

## What testing it turned up

The issue's own author tried four ways to isolate the effect and got no signal from any of them:
measuring through the cascade (`GetUserAgentStyleRules`, whole-document allocation ratios, rule-count
scaling) dilutes a diffuse, per-box saving below the noise of font loading, document setup, and
per-document variance — the same class of problem `GposPositionerAllocationTests` and
`GposTableSyntheticTests` already solved for the identical enumerator-through-an-interface shape in
the GPOS positioner (see
[2026-09-08-the-gpos-subtable-walk-allocated-an-enumerator-per-glyph.md](2026-09-08-the-gpos-subtable-walk-allocated-an-enumerator-per-glyph.md)
and
[2026-09-09-a-method-group-passed-to-getoradd-allocates-per-call.md](2026-09-09-a-method-group-passed-to-getoradd-allocates-per-call.md)).
The fix here: bump the two `DoesSelectorMatch` overloads from `private` to `internal` (there's already
a same-class precedent, `MatchesAsFirstLineSelector`), call them directly against a synthetic
`ListSelector`/`CompoundSelector` and a real fixture box in a warmed, tight loop, and assert
`GC.GetAllocatedBytesForCurrentThread()` deltas instead of measuring through the whole cascade. Verified
non-vacuous by temporarily reverting each of the three rewritten bodies back to the original LINQ and
re-running: 1,280,000–2,080,000 bytes over 10,000 calls unfixed, under 4 KB fixed, every time.

`SelectorMatchCostTests`/`SelectorsMatchOrderTests` pin down the cost ranking and the resulting index
permutation directly; `SelectorMatchOrderIndependenceTests` proves end-to-end (via rendered HTML) that
a compound selector's AND result doesn't depend on which conjunct happens to be cheap, pairing `#id`
against `:has()` (the largest gap in the cost table) in both orders.

`PeachPDF.TestHarness` gained a reusable `--benchmark [--benchmark-iterations N]` mode (default 10)
for exactly this kind of corpus-wide measurement in the future: it renders every showcase (the same
`GeneratePdf` + `Save` a real caller does, no files written) N times each after one untimed warmup,
and prints a per-showcase table plus corpus totals using `GC.GetTotalAllocatedBytes(precise: true)`
(the process-wide counter is the right one here, unlike in the parallel xUnit suite, because nothing
else runs concurrently in this process) and `Stopwatch`.

## Evidence

Full suite green on net8.0 (10,453 passed, 9 skipped - pre-existing platform-specific `MimeTypeResolver`
skips), 0 build warnings solution-wide (net8.0/net10.0/net11.0). All new/changed lines covered per a
manual line-by-line check of `coverage.cobertura.xml` (no local `diff-cover`/pip in this environment).

**Corpus-wide, Release, `--benchmark --benchmark-iterations 10`** over all 111 showcases (a second
worktree at the pre-fix commit, patched with only the new `--benchmark` flag itself so the comparison
isolates the selector-matching change):

| | main | fixed | delta |
| --- | --- | --- | --- |
| Wall time (one corpus pass) | 2674.06 ms | 2572.02 ms | -102.04 ms (-3.8%) |
| Allocated (one corpus pass) | 2449.63 MB | 2172.23 MB | -277.40 MB (-11.3%) |

Zero showcases allocated more after the fix. Biggest mover: `charts_css` (a large, selector-heavy
stylesheet) at 570.2 MB → 350.8 MB (-38.5%) - the shape of document this fix helps most, a small
number of boxes matched against many candidate rules with compound/list selectors. The 11.3% corpus-wide
figure is well above the 3.2% the issue's own narrower "26 real documents" sample measured, consistent
with the showcase corpus skewing more selector-heavy than an average document sample.
