# SVG context paint through `<use>` of a `<symbol>` or nested `<svg>`

Closes [#1500](https://github.com/jhaygood86/PeachPDF/issues/1500) and deletes the accepted gap it tracked - the
narrower residual [the earlier context-paint fix](2026-09-27-svg-context-paint-markers-use-transforms-text.md)
(issue #1469) left behind, since it never touched symbol/nested-svg content at all.

## What the load-bearing idea was

`SvgGeometryBounds.GetBoundingBox` couldn't just grow a general case for `SvgSymbolElement`/`SvgNestedSvgElement`
the way every other new element type in that switch has: every *other* consumer that reaches one of these two types
through it (clip-path `objectBoundingBox` units, mask/filter-region resolution applied directly to a bare nested
`<svg>`, or through the generic `<use>` arm) needs a *different* box - the target's own established viewport rect
(`(x, y, width, height)`, already correctly and separately special-cased by `GetOpacityGroupBounds` and
`SvgInkExtent`'s own local dispatch) - from what `ContextBounds` needs (the target's raw, pre-viewBox-mapping
children union, in the same units `RenderViewport`'s own viewBox-to-viewport matrix operates on). Reusing
`GetBoundingBox`'s existing null return for these two types was itself deliberate (a comment on
`GetOpacityGroupBounds` already said so - "`objectBoundingBox` gradient/mask/clip resolution... relies on those
types reporting null there"), so growing it with the *wrong-for-those-consumers* box would have been a real, if
narrow, regression for a directly-authored `<svg>` with its own `mask`/`filter`/`clip-path`.

The fix is a new, narrowly-scoped `SvgGeometryBounds.GetUseTargetBoundingBox`, called only from `ContextBounds`,
that special-cases exactly these two types (union of the target's own children, via the existing `UnionAll`) and
falls through to plain `GetBoundingBox` for everything else. This is the one place in this change where two
similar-looking bounding-box computations for the same element type had to stay genuinely separate rather than
unified into one shared implementation (this repo's usual convention) - the two callers need different frames, not
the same math computed twice.

Symmetric with the plain-element `RenderElementSwitch` arm's existing mechanism: `RenderViewport` now takes an
optional `contextElement` and records `matrix ∘ ambient` (the viewBox-to-viewport mapping composed with the
transform active when the viewport is entered) into `s_paintContextFrames` for the duration of the viewport's own
children's paint - mirroring how the default arm already records `target.Transform ∘ ambient`. `GetUseTargetBoundingBox`'s
box (the union of the target's own children, in the same pre-mapping units `matrix` operates on) is exactly what
that recorded frame maps out of; the two have to agree on units or `ContextBounds`'s round-trip mapping silently
produces a nonsense box.

## What running it found rather than reading

- Both new tests (`AGradientThroughAUse_OfASymbol_IsMeasuredAgainstTheSymbolsWholeContent`,
  `AGradientThroughAUse_OfANestedSvg_IsMeasuredAgainstItsWholeContent`) were confirmed to fail against the code
  before this fix - reverting just `SvgGeometryBounds.cs`/`SvgRenderer.cs` (keeping the new tests) landed both at
  r=236 (near-pure red) at a sample point that should read roughly half way between the gradient's two stops - the
  same "falls back to the painting element's own untransformed box" symptom the #1469 write-up recorded for its own
  transform-under-`use` case.
- Full `PeachPDF.Tests` suite: 14500 passed / 9 skipped / 0 failed (Debug, net8.0); 14490/9/0 (Release, net8.0 - the
  ~10-test gap between configurations is the known `Debug.Assert`-only test set, not a regression). Zero-warning
  `dotnet build PeachPDF.slnx -t:Rebuild`.
- `svg_context_paint` showcase extended with a new row (a `<symbol>` and a nested `<svg>`, each split into a solid
  grey half and a gradient `context-fill` half); rasterized with both PDFium and MuPDF, both agreeing the gradient's
  second stop colour lands correctly on the target's right half rather than a flat or wrong colour.

## Deliberately not done

`SvgGeometryBounds.GetBoundingBox` itself still returns null for a bare `<symbol>`/nested `<svg>`, and for a
`<use>` targeting either. That is not a residual gap - it is the existing, correct behavior for every consumer
*other* than `ContextBounds`, which already gets the box it actually needs (the target's own established viewport
rect) from `GetOpacityGroupBounds`/`SvgInkExtent`'s own independent special-casing.

## Evidence

`SvgContextPaintTests.AGradientThroughAUse_OfASymbol_IsMeasuredAgainstTheSymbolsWholeContent`,
`AGradientThroughAUse_OfANestedSvg_IsMeasuredAgainstItsWholeContent`. Full suite results above. `svg_context_paint`
showcase rasterized with PDFium and MuPDF, both showing the new row's gradients landing correctly.
