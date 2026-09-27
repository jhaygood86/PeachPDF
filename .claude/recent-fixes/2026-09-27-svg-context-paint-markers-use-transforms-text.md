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
helper, four-corner AABB) before folding it into the union - matching the same convention every other bbox call in
this file already relies on (a bbox excludes the element's *own* transform, since that's applied externally via the
CTM, but must include everything *below* it).

**Marker `<text>` and a filter's `FillPaint`/`StrokePaint` input were reading `run.Fill`/`run.Stroke`/`element.Fill`
directly**, bypassing `ResolveInMarker` entirely - an unresolved `SvgPaintKind.ContextFill`/`ContextStroke` then
matches none of `ResolvePaintBrush`'s/`DrawString`'s solid/gradient/pattern cases, so it silently painted nothing.
`PaintTextGlyphs`/`PaintGlyphAlongPath` now resolve `fill`/`stroke` once at the top (through `ResolveInMarker`) and use
those locals throughout, same as `PaintShape` already does; `RendererFilterInputs.PaintOf` now returns
`ResolveInMarker(...)` instead of the raw property.

## What running it found rather than reading

- Each of the three fixes was checked by reverting just `SvgRenderer.cs`/`SvgGeometryBounds.cs` (keeping the new
  tests) and confirming all four new/changed `SvgContextPaintTests` cases fail against the unfixed code - not just
  pass after the fix. The `use`-with-transform case's failure value under the old code (`r=236, b=19` - i.e. almost
  pure red) confirmed the predicted symptom exactly: the old code measured the gradient against the painting
  element's own untransformed box, indistinguishable from it having no context paint at all.
- A pattern-in-marker variant of the first test (mirroring the gradient one, same geometry) turned out to need a
  path with actual bounding-box height - a zero-height path (fine for a purely horizontal gradient, which ignores the
  height coordinate) makes `ResolvePatternRect`'s resolved tile height zero too, so `PaintPatternFill` bails out before
  drawing anything. Dropped rather than reworked, since the gradient case already exercises the exact same
  `ForMarker`/`OfContextElement` code path (both kinds share one guard).
- All 718 `PeachPDF.Tests.Svg` tests and the full suite (14472 tests) pass unchanged in both Debug and Release,
  confirming the `GetBoundingBox` group-transform fix doesn't disturb any existing (untransformed-child) group bbox
  consumer - masks, clip-path units, `<a>` link regions, ink extent.

## Deliberately not done

- A `<use>` targeting a `<symbol>`/nested `<svg>` still doesn't get a `s_paintContextFrames` entry (only the "plain
  element" render path registers one) - harmless in practice, since `SvgGeometryBounds.GetBoundingBox` doesn't
  measure those element types either, so `ContextBounds` already returns null for them before the frame would ever be
  consulted.
- The showcase's new row demonstrates the marker-gradient and `use`-transform cases visually (rasterized with both
  PDFium and MuPDF, in agreement); marker `<text>`/filter-input resolution is proven by unit test only, since it's an
  internal-plumbing fix without much visual distinctiveness beyond "paints a colour instead of nothing".

## Evidence

`SvgContextPaintTests`: `AGradientOnTheShapeOfAMarker_IsMappedThroughThePlacementTransform` (renamed/repurposed from
the old "is not carried into it" gap-pinning test), `AGradientThroughAUse_UndoesATransformBetweenTheTargetAndThePaintingElement`,
`TextInAMarker_ResolvesContextFillToThePaintOfTheShapeItIsOn`, `AFilterInputOnAMarkerShape_ResolvesContextFillToThePaintOfTheShapeItIsOn`.
Full `PeachPDF.Tests` suite (14472 passed, 9 skipped, 0 failed) in both Debug and Release, `net8.0`; zero-warning
`dotnet build PeachPDF.slnx -t:Rebuild`; diff coverage 92% (`svggeometrybounds.cs` 100%, `svgrenderer.cs` 91.1% - the
few uncovered lines are the pre-existing CFF/bitmap-font text fallback and a defensive "frame not found"/"matrix not
invertible" branch that no reachable caller currently triggers). `svg_context_paint` showcase rasterized with PDFium
and MuPDF, both showing the marker-gradient arrowhead and the `use`-transform pair correctly.
