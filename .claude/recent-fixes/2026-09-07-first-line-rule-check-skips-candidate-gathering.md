# ResolveFirstLineStyle skips both candidate-gathering passes when no ::first-line rule exists

Closes #911.

## What was wrong

`DomParser.ResolveFirstLineStyle` runs once per box, for every box in the document, and opened with two
full candidate gathers (`CssData.GetFirstLineStyleRules` for UA and author rules), each immediately
materialized with `.ToList()` - *before* the check (`firstLineUaRules.Count == 0 && firstLineAuthorRules.Count
== 0`) that makes the whole method a no-op. On a document that declares no `::first-line` rule anywhere
(the overwhelming majority of real documents), every single box still paid for two selector-index walks
and two list allocations just to learn there was nothing to do.

## The fix

Added `CssData.HasFirstLineRules`, a document-level flag computed once in `EnsureIndex` (beside the
existing `HasCascadeLayers`) by threading a `ref bool hasFirstLineRules` accumulator through the
recursive `IndexRules` walk and setting it when a style rule's selector passes the new
`CouldMatchAsFirstLineSelector` predicate. That predicate is a deliberate **superset** of the existing
`MatchesAsFirstLineSelector`: same selector-shape switch (`ListSelector`/`ComplexSelector`/
`CompoundSelector`), but the `CompoundSelector` case omits the per-box
`compound.Where(...).All(s => DoesSelectorMatch(s, box))` conjunct, since no box exists yet when this
runs once at index-build time. It can therefore return `true` for a selector that ends up matching no
box, but can never return `false` for one that would have matched some box - so `false` from every rule
in the document proves both of `ResolveFirstLineStyle`'s gathers would always return empty.
`ResolveFirstLineStyle` now opens with `if (!cssData.HasFirstLineRules) return;`, before either
`GetFirstLineStyleRules` call.

## What was deliberately not done

No change to `MatchesAsFirstLineSelector`, `GetFirstLineStyleRules`, or the per-box matching path at all
- the fix is purely an early-exit gate in front of unchanged per-box logic, so the box-level matching
semantics (including its own documented simplifications around ancestor-combinator selectors and mixed
`ListSelector` alternatives) are untouched.

## Evidence

- `dotnet test PeachPDF.Tests/PeachPDF.Tests.csproj --framework net8.0`: 9908 passed, 0 failed, 9 skipped
  (pre-existing platform-gated skips, unrelated), including the full pre-existing
  `FirstLinePseudoElementIntegrationTests` suite (all 7 implemented `::first-line` properties plus the
  structural/straddling-box/bidi cases) unchanged.
- `dotnet build PeachPDF.slnx -t:Rebuild`: 0 warnings, 0 errors.
- Diff coverage (`diff-cover` against `main`): 100% on the two changed production files.
- New tests in `src/PeachPDF.Tests/CSS/FirstLineRulesFlagTests.cs` (`FirstLineRulesFlagTests`) exercise
  `CssData.HasFirstLineRules` directly: no stylesheet, ordinary non-first-line rules, a same-pseudo-family
  distractor (`::before`), a plain tag selector, a class-compound selector, an ancestor-combinator
  selector, a `::first-line` alternative inside a `ListSelector`, and a rule nested inside `@media` - the
  false cases prove the flag doesn't false-positive on ordinary CSS, and the true cases cover every
  selector shape `CouldMatchAsFirstLineSelector`'s switch handles.
