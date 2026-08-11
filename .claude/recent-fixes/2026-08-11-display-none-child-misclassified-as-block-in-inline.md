# display:none child broke block-in-inline detection, hoisting it out of its real parent

**Symptom, not a hypothesis:** an inline-block `<select>` given a `display: none` UA-stylesheet rule
for its `<option>` children produced a corrupted box tree — the *first* `<option>` ended up as a
sibling of an anonymous block wrapper at the `<body>` level, entirely outside `<select>`, while later
`<option>`s stayed correctly nested. Found while building interactive PDF forms (#709), which
initially tried `option { display: none }` to suppress `<select>`'s option text from the static page
render and got a mangled tree instead.

**Root cause:** `CssBox.IsInline` is `false` for `display: none` (its `ActualDisplay` is `"none"`, not
`"inline"`). Two of `DomParser`'s "does this box's content need a block-in-inline split/wrap"
predicates — `ContainsInlinesOnlyDeep` and `ContainsVariantBoxes` — read `!childBox.IsInline` as
"this child is block-like" without excluding `display: none` first. A `display: none` child renders
nothing at all, so it is neither the inline half nor the block half of anything, but both predicates
counted it as "found a block" — making `<select>`'s own children look block-in-inline when descended
into from an ancestor (`ContainsInlinesOnlyDeep` has no atomic-inline-level exemption for
`inline-block`, unlike `inline-flex` — see below), which triggered `CorrectBlockInsideInlineImp` on
`<body>` and split its child list at the first "offending" (display:none) node.

**What was deliberately NOT done:** the obvious-looking fix — adding `inline-block` (and
`inline-grid`/`inline-table`) to `DomParser.IsAtomicInlineLevel`, which already exempts `inline-flex`
from this same descent — was rejected. That function's own doc comment already explains why: unlike
`inline-flex`, no layout branch places `inline-block`/`inline-grid`/`inline-table` atomically once the
split is skipped (tracked as issue #473), so a *genuine* block child inside one of those would go from
"rendered wrong" to "rendered nothing" — a worse regression than the one being fixed. The actual fix
instead teaches `ContainsInlinesOnlyDeep` and `ContainsVariantBoxes` to skip `display: none` children
entirely (`continue`, not "count as block") — a `display: none` box can never be the genuine block
content #473 is about, since it never renders, so this is safe regardless of #473's own resolution.

**Evidence:** `AtomicInlineLevelBoxCorrectionTests` (`src/PeachPDF.Tests/Integration/`) — a failing
repro against the pre-fix code (inline-block `<select>` with two `display:none` `<option>` children,
first one hoisted out), a positive inline-table case, and a regression guard confirming the existing
inline-flex handling (issue #462's own fix) still holds. Full `dotnet test --framework net8.0` suite
(8618 tests) passes with no other regressions — this path is exercised broadly, not just by the new
test.
