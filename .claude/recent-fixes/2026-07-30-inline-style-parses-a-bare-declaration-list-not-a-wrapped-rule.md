# `style="..."` parses a bare declaration list instead of a wrapped `"* { ... }"` rule

## The load-bearing idea

Re-profiling the showcase workload after the `CascadeApplyStyles` defaulting-loop fix (this same day's
other recent-fixes entry) showed `DomParser.CascadeApplyStyles` still on top, but down from 29.5% to 19.4%
of inclusive time - confirming that fix, and pointing at what was left. One remaining, verifiably-wasted
piece: an element's inline `style="..."` attribute was parsed by wrapping its text as `"* { " + text + " }"`
and running the *entire* stylesheet pipeline (`CssParser.ParseStyleSheet` → `StylesheetParser.Parse` →
`Lexer`/`TextSource` construction, `StylesheetComposer.CreateRules`, selector tokenizing/matching for the
throwaway `*`, a full `Stylesheet`/`StylesheetText`/`TextRange` wrapper) just to reach the one
`StyleDeclaration` `AssignCssBlock` actually reads (`stylesheetRule.Style` - nothing downstream ever reads
the synthetic rule's `Selector`/`SelectorText`/`NestedRules`/specificity).

A `style="..."` attribute's text is always a flat declaration list per spec - never a full rule (no
selector, no braces) - and `StylesheetParser` already exposes exactly the right primitive for that:
`AppendDeclarations(StyleDeclaration, string)`, which runs `StylesheetComposer.FillDeclarations` directly
against a caller-supplied `StyleDeclaration` with no selector/rule/brace handling at all. `DomParser.cs`
already used this same primitive (via `StylesheetParser.Default.ParseDeclaration(...)`) elsewhere in the
same file, for the `var()`-reparse path - confirming it's an established, safe pattern. The inline-style
parse now constructs a bare `StyleRule` (whose constructor already gives it a default "match everything"
selector and an empty `StyleDeclaration` - both correct to leave alone, since only `.Style` is ever read)
and fills it directly via `StylesheetParser.Default.AppendDeclarations`, skipping the wrap-string
allocation and the unused selector/rule machinery entirely.

## Evidence

Full net8.0 suite: 6991 passed / 0 failed / 9 skipped (unchanged pass count - this is a pure internal
parsing-path change with no observable behavior difference, and the existing suite already exercises
inline style extensively, including `!important` and custom properties in `style="..."`, per
`GlobalKeywordCascadeTests.cs`/`CustomPropertiesIntegrationTests.cs`). Zero-warning
`dotnet build PeachPDF.slnx -t:Rebuild`. Diff coverage 96% (gate 90%); `DomParser.cs` itself at 100%.

Showcase workload (all 73 showcases, one full pass), cumulative effect of all three of this session's
fixes (dead `InheritStyle()` removal, the defaulting-loop skip, and this inline-style change), measured
against current `origin/main`:

| Metric | main | With all 3 fixes | Delta |
|---|---|---|---|
| Wall clock (mean of 3 runs) | 19.62s | 18.02s | **-8.1%** |
| Peak RSS (mean of 3 runs) | 200,832 KB | 200,324 KB | ~flat |
| Gen0 GC count (one run) | 2,606 | 1,622 | **-37.8%** |
| Gen1 GC count (one run) | 163 | 137 | -16.0% |
| Gen2 GC count (one run) | 30 | 26 | -13.3% |
| Allocated bytes (one run) | ~44.6 GB | ~27.7 GB | **-38.0%** |

These numbers are consistent with (not meaningfully better than) the defaulting-loop fix's own
measurement in isolation - most of the win is that fix; this one adds a modest amount on top, as expected
given inline-style declarations are a smaller fraction of total cascade work than the defaulting loop's
per-box, per-property sweep.

**Also compared against the `v0.9.6` release tag** (a much older point - 58 showcases existed then vs 73
today, so raw totals aren't a clean apples-to-apples run-to-run comparison; normalizing by showcase count
for a rough per-document read):

| Metric | v0.9.6 (58 showcases) | Today (73 showcases) | Per-showcase delta |
|---|---|---|---|
| Wall clock | 15.51s (~267 ms/showcase) | 18.02s (~247 ms/showcase) | -7.5% |
| Allocated bytes | ~32.4 GB (~558 MB/showcase) | ~27.7 GB (~379 MB/showcase) | **-32.0%** |
| Gen0 GC count | 1,883 (~32.5/showcase) | 1,622 (~22.2/showcase) | -31.5% |
| Gen1 GC count | 170 (~2.9/showcase) | 137 (~1.9/showcase) | -35.9% |
| Gen2 GC count | 16 (~0.28/showcase) | 26 (~0.36/showcase) | +29% (small absolute counts either side; likely reflects newer showcases' content, e.g. larger images/fonts, more than a regression) |

Despite doing ~26% more work (more, and generally more complex, showcases), today's build allocates less
in absolute terms than `v0.9.6` did - the per-showcase numbers make the underlying improvement clearer.
This reflects the cumulative effect of everything between the two points, not only this session's three
fixes, so it's a "how far has the whole codebase come" data point rather than isolated evidence for any
one change - recorded here because it was asked for, not as attributed proof of this fix specifically.
