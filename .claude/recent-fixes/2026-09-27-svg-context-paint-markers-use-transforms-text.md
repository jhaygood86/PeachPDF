# SVG context paint: gradients/patterns in markers, a transform under `use`, marker `<text>` and filter inputs

`SvgRenderer.cs` closes the three deviations the context-paint PR left as an accepted gap. Closes issue #1469 and
deletes [the accepted gap](../accepted-gaps/svg-context-paint-gradients-in-markers-transforms-under-use-and-marker-text.md).

## What the load-bearing idea was

**A gradient/pattern context paint needs the same "measure in the context element's own frame, then map into
whichever frame the paint actually resolves in" treatment everywhere it can occur - not just for `<use>`.** The
existing (working) `<use>` case already did half of this: `ContextBounds` measured the box against the `use`'s target
in the target's own space. What was missing was the *mapping* half (the box was used as-is, correct only when the
painting element sat in exactly that same frame) and doing the same thing for a marker's own shape, which the old
code sidestepped entirely by collapsing a gradient/pattern to `SvgPaint.None` (`ForMarker`) rather than carrying it
through.

The fix generalizes the mechanism `use` already needed: `SvgPaint.OfContextElement` tags a paint with whichever
element's coordinate space it must be measured in (already existed, used only by `use`); a new thread-static
`s_paintContextFrames: Dictionary<SvgElement, RMatrix>` records that element's own frame (`RGraphics.CurrentTransform`
right as its own content begins painting) for as long as content under it can reference it, mirroring the existing
`s_markerContext` save/restore pattern exactly (push on entry, restore the previous value - not just remove - on
exit, since a cyclic/re-entrant paint or pattern replay can revisit the same key). `ForMarker` now calls
`paint.OfContextElement(element)` instead of discarding a gradient/pattern - a no-op if the paint already names a
context element from further out (nested markers/`use`, "nearest one wins", unchanged). `ContextBounds` gained a
`RGraphics g` parameter and, once it has the raw box (in the context element's own frame), maps it through
`contextFrame.Then(inverse(g.CurrentTransform))` - the frame recorded when that context element's content began
painting, composed with the inverse of whatever frame is active *now* - before handing it to
`ResolvePaintBrush`/`ResolveStrokePen`/`PaintPatternFill` as `boundsOverride`. For a marker this undoes exactly the
placement matrix (position/rotation/scale); for `use` it undoes any `transform` sitting between the target and the
actual painting element, including one several groups deep.

**`SvgGeometryBounds.GetBoundingBox`'s group case was silently ignoring every child's own `transform`** - `UnionAll`
unioned each child's raw local bbox with no attempt to compose it through `child.Transform` first, at every level of
nesting. This was invisible until something needed the *correct* combined bbox of a `use`-instantiated subtree that
has an internal transform (exactly the second deviation): a `<g>` wrapping a translated/scaled child unioned in that
child's *untransformed* geometry, silently wrong the moment there was more than one child or the transform did
anything besides pure identity. Fixed by mapping each child's bbox through its own `Transform` (a new `TransformBounds`
helper, four-corner AABB, now `internal` so `SvgRenderer` can reuse it - see below) before folding it into the union -
matching the same convention every other bbox call in this file already relies on (a bbox excludes the element's
*own* transform, since that's applied externally via the CTM, but must include everything *below* it).

**Marker `<text>`, its `text-decoration`, and a filter's `FillPaint`/`StrokePaint` input were reading
`run.Fill`/`run.Stroke`/`decorator.Fill`/`element.Fill` directly**, bypassing `ResolveInMarker` entirely - an
unresolved `SvgPaintKind.ContextFill`/`ContextStroke` then matches none of `ResolvePaintBrush`'s/`DrawString`'s
solid/gradient/pattern cases (or, for the decoration's `Kind == Solid` check, falls back to black), so each silently
painted nothing (or black). `PaintTextGlyphs`/`PaintGlyphAlongPath` now resolve `fill`/`stroke` once at the top
(through `ResolveInMarker`) and use those locals throughout, same as `PaintShape` already does; `DrawDecorationSpan`
resolves `decorator.Fill` the same way before its `currentColor`-fallback check; `RendererFilterInputs.PaintOf` now
returns `ResolveInMarker(...)` instead of the raw property.

## A second, review-caught bug: `RGraphics.CreateTile`'s tile didn't continue the caller's coordinate space

`s_paintContextFrames`/`ContextBounds` compares a context element's recorded frame against `g.CurrentTransform` at
consumption time - correct only when both readings come from the same coordinate-space continuity. That continuity
breaks across a **`CreateTile`** boundary specifically: `RenderContainerOpacityGroup`, `RenderMaskedElementContent`/
`BuildMaskTile`, and `PaintPatternFill`'s own tile all paint their content into a fresh `RGraphics` whose
`CurrentTransform` started at `RMatrix.Identity` rather than continuing the caller's - unlike `BeginRasterSurface`
(used only by the raster filter path), which already seeded it via `SeedTransform`. A `<use>` targeting a container
with its own `opacity`/`mask`, or a shape filled through a `<pattern>`, whose content used `context-fill`/
`context-stroke` resolving to a gradient/pattern, therefore composed the context frame against the wrong baseline -
a materially wrong (not just imprecise) box. The raster **filter** path (`RendererFilterInputs.PaintPaint`, called
through `BeginRasterSurface`) was already correct and needed no change.

Fixed by giving `CreateTile` the same contract `BeginRasterSurface` already has: `GraphicsAdapter.CreateTile` and
`RasterGraphics.CreateTile` now seed the new tile's own `CurrentTransform` from the caller's (a new
`GraphicsAdapter.SeedTransform`, mirroring `RasterGraphics`'s existing one), and `RGraphics.CreateTile`'s own doc
remarks now state this so the next raster-tile feature can't silently diverge the way these two already had. This is
bookkeeping only - neither backend's *real* geometry changes, since a `GraphicsAdapter` tile's actual PDF content
positions itself through the Form XObject's own native graphics-state CTM (untouched by seeding the bookkeeping
`_accumulated` field), and a `RasterGraphics` tile's actual pixel geometry is governed by the tile surface's own
grid/pitch (also untouched by seeding `_layoutCtm`).

**A parallel bug in the same family, also review-caught:** `SvgRenderer`'s own `GetOpacityGroupBounds`/
`UnionOpacityGroupBounds` (a second, renderer-local "union children's bounding boxes" pass that extends
`SvgGeometryBounds.GetBoundingBox` to size an opacity-group's tile) had the *exact* same "ignores a child's own
transform" bug this PR had just fixed in `SvgGeometryBounds.UnionAll` - except this copy wasn't touched, and its own
doc comment claimed parity with `SvgGeometryBounds` that was no longer true once that one was fixed. Fixed the same
way (composing each child's `Transform` via the now-`internal` `SvgGeometryBounds.TransformBounds`), and corrected
the stale comment.

## What running it found rather than reading

- Each of the three original fixes was checked by reverting just `SvgRenderer.cs`/`SvgGeometryBounds.cs` (keeping the
  new tests) and confirming all four new/changed `SvgContextPaintTests` cases fail against the unfixed code - not
  just pass after the fix. The `use`-with-transform case's failure value under the old code (`r=236, b=19` - i.e.
  almost pure red) confirmed the predicted symptom exactly: the old code measured the gradient against the painting
  element's own untransformed box, indistinguishable from it having no context paint at all.
- The `CreateTile`-seeding bug was found by a review pass, not by any test this PR had already written - none of the
  original four tests route through an opacity/mask/pattern tile. Confirmed real (not a false alarm) by reading
  `GraphicsAdapter`'s actual field usage line by line: `_accumulated` is pure bookkeeping (exposed only via
  `CurrentTransform`, never read by any drawing method - the real transform is `_g`'s own native PDF graphics state,
  pushed separately in the same `PushTransform` call), which is exactly why seeding it is safe and doesn't
  double-apply anything to what's actually drawn. A parallel claim that the *filter* path had the same bug was
  checked the same way and found false: `BeginRasterSurface` already seeds (`SeedTransform`), confirmed by tracing
  `RendererFilterInputs.PaintPaint`'s own call site.
- A new regression test (`AGradientThroughAUse_IsMeasuredCorrectlyWhenTheTargetPaintsThroughATile`) reverts the same
  way: commenting out just the new `SeedTransform` call flips its qualitative assertion (`b > r`) to fail with
  `r=82, b=46` - the wrong side of the midpoint, not merely an imprecise value.
- A pattern-in-marker variant of the first test (mirroring the gradient one, same geometry) turned out to need a
  path with actual bounding-box height - a zero-height path (fine for a purely horizontal gradient, which ignores the
  height coordinate) makes `ResolvePatternRect`'s resolved tile height zero too, so `PaintPatternFill` bails out before
  drawing anything. Dropped rather than reworked, since the gradient case already exercises the exact same
  `ForMarker`/`OfContextElement` code path (both kinds share one guard).
- Full suite (14501 tests) passes unchanged in both Debug and Release, confirming neither the `GetBoundingBox`/
  `GetOpacityGroupBounds` group-transform fixes nor the `CreateTile` seeding change disturb any existing
  (untransformed-child, or transform-free) consumer - masks, clip-path units, `<a>` link regions, ink extent, every
  other opacity/mask/pattern tile in the corpus.

## Deliberately not done

- A `<use>` targeting a `<symbol>`/nested `<svg>` still measures a gradient/pattern context paint against the wrong
  box - `SvgGeometryBounds.GetBoundingBox` has no case for either (both need their own viewBox-to-viewport mapping
  replicated in a measurement-only pass, not just plain geometry), so `ContextBounds` still falls back to the
  painting element's own box. Filed as [#1500](https://github.com/jhaygood86/PeachPDF/issues/1500) and recorded in
  [its own accepted gap](../accepted-gaps/svg-context-paint-gradients-through-use-of-symbol-or-nested-svg.md) -
  narrower and separately-scoped from the three deviations #1469 itself named, which never touched symbol/nested-svg
  content.
- The showcase's new row demonstrates the marker-gradient and `use`-transform cases visually (rasterized with both
  PDFium and MuPDF, in agreement); marker `<text>`/its decoration/filter-input resolution and the tile-seeding fix
  are proven by unit test only, since they're internal-plumbing fixes without much visual distinctiveness beyond
  "paints the right colour instead of the wrong one or none".

## Evidence

`SvgContextPaintTests`: `AGradientOnTheShapeOfAMarker_IsMappedThroughThePlacementTransform` (renamed/repurposed from
the old "is not carried into it" gap-pinning test), `AGradientThroughAUse_UndoesATransformBetweenTheTargetAndThePaintingElement`,
`TextInAMarker_ResolvesContextFillToThePaintOfTheShapeItIsOn`, `TextInAMarker_ResolvesContextFillForItsTextDecorationToo`,
`AFilterInputOnAMarkerShape_ResolvesContextFillToThePaintOfTheShapeItIsOn`,
`AFilterInputOnAMarkerShape_WithAGradientContextFill_IsMappedThroughThePlacementTransform`,
`AGradientThroughAUse_IsMeasuredCorrectlyWhenTheTargetPaintsThroughATile`. Full `PeachPDF.Tests` suite (14501 passed,
9 skipped, 0 failed) in both Debug and Release, `net8.0`; zero-warning `dotnet build PeachPDF.slnx -t:Rebuild`; diff
coverage 96% (`svggeometrybounds.cs`/`graphicsadapter.cs`/`rastergraphics.cs` 100%, `svgrenderer.cs` 95.7% - the few
uncovered lines are the pre-existing CFF/bitmap-font text fallback and a defensive "frame not found"/"matrix not
invertible" branch that no reachable caller currently triggers). `svg_context_paint` showcase rasterized with PDFium
and MuPDF, both showing the marker-gradient arrowhead and the `use`-transform pair correctly.
