# `vertical-align` no longer inherits from an ancestor (issue #530)

_Landed 2026-08-01._

**`vertical-align` no longer inherits from an ancestor** (tracked deviation from CSS 2.1 §10.8.1,
`Inherited: no`): removed `"vertical-align"` from `CssDefaults.InheritedProperties`
(`src/PeachPDF/Html/Core/CssDefaults.cs`) and mirrored the existing `unicode-bidi` handling in
`CssBox.InheritStyle` (`src/PeachPDF/Html/Core/Dom/CssBox.StyleProperties.cs`) — capture the box's own
pre-inherit `VerticalAlign` before the whole-`TextArea` adopt, restore it right after in the
`!everything` branch. Unlike `box-sizing` (which lives in the never-whole-adopted `BoxModel` area and
so needs an explicit copy in the `everything`-only structural-duplicate section too), `VerticalAlign`
needs no such extra copy: an initial attempt added one anyway on the assumption it paralleled
`box-sizing`, but a review pass caught that it was dead code — `TextArea` is already whole-adopted by
reference earlier in the method and never un-adopted when `everything: true` (only the `!everything`
branch restores it), so `_computedStyle.Text` already equals `parentStyle.Text` by the time such a
block would run, making any further copy a guaranteed no-op. Removed before landing.

Two consumers depended on the old (non-compliant) unconditional inheritance and needed real fixes,
not just the `InheritedProperties` removal — found by running the layout test suite, not by reading
the code:

1. **`DomParser.ResolveFirstLineStyle`'s `::first-line` shadow box.** It builds a fresh, tagless
   `CssBox` and calls `InheritStyle(box)` to seed it from the real box's already-resolved style. With
   vertical-align no longer copied there, the shadow box's `VerticalAlign` fell back to its own
   initial `"baseline"` regardless of what the real box had — so `CssLayoutEngine
   .ApplyVerticalAlignment`'s `firstLineStyle.VerticalAlign != ownerBox.VerticalAlign` heuristic (used
   to detect "did some `::first-line` rule actually declare vertical-align") started firing for *any*
   matched `::first-line` rule on a block with a non-baseline `vertical-align`, even one that only sets
   `color`. Fixed with one explicit line, `shadowBox.VerticalAlign = box.VerticalAlign;`, right after
   `InheritStyle` — deliberately not `InheritStyle(box, everything: true)`, which would have also
   pulled in Background/Border/VisualEffects/BoxModel/etc. onto the shadow box, changing untested
   `::first-line` behavior out of scope for this fix. Locked in by
   `FirstLinePseudoElementIntegrationTests.VerticalAlign_OwnerBoxsOwnNonBaselineValue_DoesNotSpuriouslyTriggerFirstLineOverride`
   (must fail without the fix) and its positive-case counterpart
   `VerticalAlign_GenuineFirstLineOverride_StillAppliesWhenOwnerHasNonBaselineValue`.

2. **Anonymous (tagless) text-run boxes — the load-bearing discovery.** Every text node becomes its
   own anonymous `CssBox` child (`CssBox.CreateBox`, called from `HtmlParser.AddTextBox`), and this is
   the box that actually owns the line-box rectangle `CssLayoutEngine.ApplyVerticalAlignment` reads
   `.VerticalAlign` from — not the styled `<span>`/etc. it sits inside. An anonymous box never goes
   through its own CSS cascade (no selector ever matches a tagless box), so before this fix it relied
   entirely on unconditional inheritance to pick up its parent element's `vertical-align`; once that
   stopped, EVERY inline element's own explicit `vertical-align` (not just inherited cases) silently
   stopped applying, because the box actually consulted for alignment was always stuck at the anonymous
   child's own initial `"baseline"`. This is not a corner case — it broke the full existing
   `VerticalAlignIntegrationTests` suite (`top`/`bottom`/`middle`/`text-top`/`text-bottom` all
   collapsed to the same position) and would have shipped invisibly if that suite hadn't already
   existed. Fixed in `ApplyVerticalAlignment` itself: walk up to the nearest ancestor with an
   `HtmlTag` before reading `.VerticalAlign` (reusing the exact walk `text-top`/`text-bottom` alignment
   already needed for its reference-font lookup, for the same underlying reason), rather than touching
   `CreateBox` or the cascade — kept the fix local to the one method that actually needs the resolved
   value, instead of teaching anonymous-box creation a new "which non-inherited properties should still
   propagate" rule that every future non-inherited property would have to remember to extend.

Also fixed for free by the `InheritedProperties` removal alone (no separate code change):
`vertical-align: unset` now resolves to `initial` (`baseline`) instead of behaving like `inherit`, and
so does the `var()` guaranteed-invalid fallback (`DomParser.GetGuaranteedInvalidFallback`, which
reuses the same `InheritedProperties.Contains` check).

Not affected: `ApplyCellVerticalAlignment`/table-cell alignment — `td, th { vertical-align: inherit }`
in the UA stylesheet (`CssDefaults.cs`) uses the always-unconditional explicit-`inherit` cascade-keyword
path, never gated on `InheritedProperties`.

Evidence: full suite `dotnet test PeachPDF.Tests/PeachPDF.Tests.csproj --framework net8.0` — 7391
passed, 0 failed (9 pre-existing, unrelated skips). `dotnet build PeachPDF.slnx -t:Rebuild` — 0
warnings. `diff-cover` against `origin/main` — 100% (13/13 coverable lines).
