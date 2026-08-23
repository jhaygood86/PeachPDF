# `vi`/`vb`/`cqi`/`cqb` now follow the root element's/query container's own `writing-mode`

Issue [#795](https://github.com/jhaygood86/PeachPDF/issues/795). Closes
`.claude/accepted-gaps/viewport-and-container-inline-block-units-ignore-writing-mode.md`. `vi`/`vb` (CSS
Values and Units 4 §6.2) and `cqi`/`cqb` (CSS Containment 3 §6.2) were resolved as plain physical aliases
of `vw`/`vh`/`cqw`/`cqh` regardless of anyone's `writing-mode` — under `vertical-rl`/`vertical-lr` they now
correctly rotate onto the orthogonal physical axis instead.

## The load-bearing idea

`cqw`/`cqh`/`vw`/`vh` are genuinely, spec-definedly **physical** (confirmed against the actual spec text,
not assumption): `cqmin`/`cqmax` are the smaller/larger of `cqi`/`cqb` (not `cqw`/`cqh`), while `vmin`/
`vmax` stay the smaller/larger of `vw`/`vh` (not `vi`/`vb`) — an asymmetry between the two unit families
worth knowing before touching either again. Since a single `Length.ToPixels` call resolves an arbitrary
unit without knowing in advance which family it belongs to, the physical pair (`containerWidthPt`/
`containerHeightPt`, `viewportWidthPt`/`viewportHeightPt`) and the writing-mode-aware logical pair
(`containerInlineSizePt`/`containerBlockSizePt`, `viewportInlineSizePt`/`viewportBlockSizePt`) had to
become four genuinely separate parameters, not one pair reused for both — the two families only happen to
agree numerically under `horizontal-tb`, and diverge for real once a vertical writing mode is involved.
This threads through every existing call site that already carried the old two-pair signature
(`CssBox.GetContainerRelativeUnitBasis`/`GetViewportUnitBasis`, both `CssValueParser.ParseLength`
overloads, `CalcEvaluator.CalcContext`, both `FontSizeResolver.Resolve` overloads, `DerivedStyle`'s
`ActualFont`) — mechanical but real, since the physical pair still has to reach every one of those call
sites unchanged for `cqw`/`vw` to keep working.

`vi`/`vb` need the **root** element's own writing-mode, not the resolving box's — a new
`HtmlContainerInt.RootWritingMode`, set once per document parse in `DomParser.GenerateCssTree` right after
`CascadeApplyStyles` runs (reusing the exact same `DomUtils.GetBoxByTagName(root, "html")` lookup
`DocumentLanguage` already did earlier in the same method — importantly, *after* cascade, not alongside
the pre-cascade `DocumentLanguage` assignment, since the `<html>` box's own `WritingMode.Value` isn't
resolved until the cascade actually runs). Caching this once, rather than walking the DOM inside
`GetViewportUnitBasis`, matters because that method runs on effectively every CSS length resolved in the
document — a per-call DOM walk would have been a real perf regression, not just inelegant.

`isVertical`/`rootIsVertical` deliberately use the narrower `VerticalRl`/`VerticalLr`-only test
`WritingModeFrame.IsVertical` already uses (not `LogicalPropertyResolver.BlockStart`'s broader switch,
which also maps `SidewaysRl`/`SidewaysLr`) — matching this engine's existing, deliberate scope boundary
that `sideways-*` still renders as `horizontal-tb` throughout (issue #766, still open). Treating a
`sideways-*` root/container as "vertical" here while nothing else in the engine does would have made
`vi`/`vb`/`cqi`/`cqb` disagree with the box's own actual layout.

## Two more, genuinely separate bugs found and fixed in the same change

Testing `cqi`/`cqh` end-to-end through a real `HtmlContainerInt.PerformLayout` pass surfaced that a
descendant box's own width, resolved top-down during a `container-type: size` container's child-layout
phase, read that container's `ClientBottom` (hence its physical height) as `0` — not because of anything
writing-mode-related, but because this engine doesn't settle a box's own `ClientBottom` for a *definite*
(non-auto) height until that box's own layout epilogue (`CssLayoutEngine.ApplyHeight`) runs, well after its
children's own width has already been resolved. Reproduced against a plain `horizontal-tb`
`container-type: size` container's `cqh` too (no vertical writing-mode involved at all), confirming it
predates this change and isn't specific to the inline/block split. Filed and fixed as
[#805](https://github.com/jhaygood86/PeachPDF/issues/805): a new `CssBox.ResolveDefiniteHeightPt()`
resolves a box's own **definite, non-auto** height directly from its `Height` string instead of reading the
live, not-yet-settled `ClientBottom` — calling `CssLayoutEngine.GetBoxHeight` for the actual computation
(reusing its existing `min-height` clamp and percentage base, rather than re-deriving that logic
independently) and then mirroring `ApplyHeight`'s own `max-height`/min-height-wins-on-conflict clamp on
top. This is deliberately narrow — only the case that has a content-independent answer at all — not a
general reordering of when `ApplyHeight`/`ActualBottom` settle for every box, which would be the
"materially bigger change" this work correctly declined to attempt, per
`.claude/invariants/fragmentation-a-boxs-own-measurements-are-only-valid-at-specific-times.md`'s own
warning that a box's measurements are only valid at specific times. A first cut of this fix hand-rolled the
height computation instead of calling `GetBoxHeight`, which a review pass caught missing both the
`min-height`/`max-height` clamp and `GetBoxHeight`'s own percentage base (`PercentageBase`, which differs
from plain `ContainingBlock` for an absolutely-positioned box) — silently wrong numbers for a container
that also declares `min-height`/`max-height`, not just stale ones. Fixed by delegating to `GetBoxHeight`
for the base computation instead of duplicating it. A **percentage** `height` still returns `null` when its
own base isn't itself height-calculated yet (the same timing gap, recursively, on the containing block) —
tracked separately as [#807](https://github.com/jhaygood86/PeachPDF/issues/807), since resolving that
generally needs the same early-resolution treatment applied up an arbitrary ancestor chain, a genuinely
bigger change. `HtmlContainerInt.BuildContainerQuerySizes()` (the `@container` *condition*-matching size
snapshot, as opposed to the *unit*-resolution path above) was never affected by this timing gap in the
first place — it only runs after `PerformLayoutOnePass` has fully finished, so every box's `ClientBottom`
is already real and settled by then.

A post-change review pass also caught a sibling, adjacent bug: `@container (inline-size ...)`/
`(block-size ...)` *condition* matching (`HtmlContainerInt.BuildContainerQuerySizes`/
`ContainerQueryMatcher.FeatureMatches`) had exactly the same writing-mode-blind `width`≡`inline-size`/
`height`≡`block-size` conflation the length-unit fix above closes for `cqw`≠`cqi`/`cqh`≠`cqb` — a sibling
code path evaluating `@container` conditions rather than resolving CSS length units. Confirmed against the
real CSS Containment 3 §7.3 spec text (`width`/`height` are physical; `inline-size`/`block-size` are the
container's own writing-mode-relative axis; `aspect-ratio`/`orientation` are defined via `width`/`height`,
staying physical too). Filed and fixed as
[#806](https://github.com/jhaygood86/PeachPDF/issues/806): `ContainerQueryContext` gained the same
physical/logical 4-field split `CssBox.GetContainerRelativeUnitBasis` already has (`WidthPt`/`HeightPt` for
`width`/`height`/`aspect-ratio`/`orientation`; `InlineSizePt`/`BlockSizePt` for `inline-size`/`block-size`),
computed in `BuildContainerQuerySizes` with the identical `WritingMode.Value is VerticalRl or VerticalLr`
swap `GetContainerRelativeUnitBasis` uses (safe here without `ResolveDefiniteHeightPt`'s help, since this
method already only runs post-layout). The same review pass also caught `ContainerQuerySizes.SizesEqual`
(the convergence loop's pass-over-pass "did anything change" check) comparing only `InlineSizePt`/
`BlockSizePt`, never the new `WidthPt`/`HeightPt` — harmless for a `horizontal-tb` container (the two pairs
are always equal there) but a latent trap for a container whose own `writing-mode` itself changes between
refinement passes in a way that could leave `InlineSizePt`/`BlockSizePt` coincidentally unchanged while
`WidthPt`/`HeightPt` (what `width`/`height`/`aspect-ratio`/`orientation` actually read) genuinely differ.
Fixed by comparing all four fields.

## Evidence

New unit tests in `LengthTests.cs` (proving `Length.ToPixels`'s split switch arms read the correct
parameter, not just that the old expected values still hold), `ContainerAndViewportUnitBasisTests.cs`
(direct `CssBox.GetContainerRelativeUnitBasis`/`GetViewportUnitBasis` coverage, including dedicated
`ResolveDefiniteHeightPt` regression cases for the no-live-geometry-yet case and for `min-height`/
`max-height` clamping), and new integration tests in `ContainerQueryLayoutIntegrationTests.cs`/
`ViewportUnitLayoutIntegrationTests.cs` exercising `cqw`/`cqh`/`cqi`/`cqb`, `@container
(inline-size/block-size/width/height)`, and `vi`/`vb`/`vw`/`vh` end-to-end through real layout against
`vertical-rl` containers/roots and a definite-height `size` container. Full existing suite (9151+ tests
after these additions) passing unchanged, including the previously-broken `cqh`/`cqi` cases now genuinely
exercised end-to-end instead of only at the `CssBox` level.
