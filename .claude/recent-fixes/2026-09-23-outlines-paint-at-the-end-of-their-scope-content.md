# Outlines paint after their scope's content, not their box (#1268)

**Symptom:** in the `outline` showcase's layout-neutral example, the next sibling (`after`, gray
background) stayed readable through a 20px ring. Chromium's print covers it.

**Cause:** `FragmentPainter.PaintBoxContent` drew a box's outline as the last thing in *that box's*
paint. The next sibling then painted its background over the part of the ring that spills outside
the box, and an outline spills outside its box by definition.

**Fix:** outlines are collected into the nearest *outline scope* (`FragmentPainter.Outlines.cs`).
`PaintBoxContent` now sets positive-`z-index` layers (`StackingOrder.LayerOf > 0`) aside and paints
them last (`PaintLayer`), after the collapsed table borders, the box's own outline entry, the marker
and the content image. That is Appendix E's step 9, and it fixed a pre-existing ordering bug too: a
raised child used to paint *under* its parent's collapsed borders and marker. When the box opened
the outline scope, its collected outlines are drawn just before those raised layers. The box's
`overflow` clip is re-pushed around them, because it was popped before the marker. Whatever is
collected later, or everything when there is no raised layer, is drawn when the scope closes. The
scope opens and closes in `PaintTagged`, outside the box's structure-element wrapper, and records
its owner fragment so only that box draws early. Deferred outlines are drawn as one `Artifact`. A
scope is `EstablishesOutlineScope`:

- the root, stacking contexts, positioned boxes, and out-of-flow boxes. This covers every box
  `StackingOrder.Flatten` can hoist, so a deferred outline never crosses the ancestor-clip replay
  that `PaintStackingParticipant` does for a hoisted participant.
- boxes that `PaintFragment` wraps in graphics state: transform, `clip-path` (not a stacking context
  in `IsStackingContextBox`), and opacity/blend/filter (already stacking contexts). These scopes
  flush *inside* the push, so the outline stays in the transform, clip, and opacity tile.
- atomic inlines and flex/grid items. Browsers paint these all-phases-atomically, outlines included.

The only state between a scope and a deferred box is `overflow` clips. Each deferred entry snapshots
the `(OverflowClip, OverflowClipCurve)` steps pushed since the scope opened. Consecutive identical
steps are deduplicated, because every box under one clipping ancestor carries that same clip. The
entries are replayed through `RenderUtils.ClipGraphicsByOverflow` at flush time.

**Traps:**
- Every box collects `outlinePaints` rectangles whether or not it has an outline. The first cut
  deferred all of them, so every scope opened an empty artifact and replayed rounded clip paths. Five
  tagged/clip-curve tests caught it. `OutlineDrawHandler.Paints` now gates the deferral.
- The relative order of outlines is unchanged (entries are appended where the draw used to be:
  post-order), so only their position relative to *other* content moves.
- Why before positive `z-index` and not at the very end: the first version drew at scope end (the
  spec's step 10, what Firefox does). That showed rings through `z-index: 5` overlays, which is the
  content authors raise *to* cover things, and moved away from Chromium, which #1268 was about
  matching. Going back means drawing in `CloseOutlineScope` only: drop the early draw ahead of the
  raised layers in `PaintBoxContent` (the scope-owner tracking then has nothing to do).
- The first draw-before-raised-layers version drew *inside* the layer loop. That put the outlines
  under the collapsed borders and marker painted after the loop, which a review caught. Painting the
  raised layers last is what fixed it; don't move the draw back into the loop.
- The early draw runs inside the scope box's own tag wrapper, unlike the scope-close draw. A box
  tagged `-peachpdf-pdf-tag-type: artifact` has its BMC open there, and the outline's own artifact
  nested inside it; a review caught that. `StructureTagBuilder` now counts open sequences
  (`IsInMarkedContent`), and `DrawOutlines` opens an artifact only when none is open. Inside an
  artifact the rings already are one; inside a content element they would join its content (where
  an outline was always tagged before deferral). A plain box with text isn't a content element: its
  text goes into an anonymous child, so the `<div …>text<span z-index:1>` case the reviews flagged
  opens no BDC. `TaggedPdfPaintOrderTests.Outline_DrawnUnderAPositiveZIndexChild_IsNeverNestedInMarkedContent`
  covers both.
- Found while checking that: on `main`, an inline `<span>` containing an absolutely positioned child
  draws no outline at all (plain text or a `<b>` child is fine). That predates this change and is
  tracked as #1299, not fixed here.
- Where this still differs from Chromium: Chromium also draws a layer's outlines *before* its
  `z-index: auto`/`0` positioned children. We draw them after those, deliberately, so a following
  `position: relative` sibling cannot cover a ring the way a following in-flow one used to.

**Not done:** replaced elements (`ReplacedFragmentPainter`) still paint no outline at all. That is a
separate gap, unchanged here.

**Evidence:** new `OutlinePaintOrderIntegrationTests` (ordered log: ring after the following
sibling's background and text, scope ends before a later positioned sibling, inline-block atomicity,
overflow clip replay, ring inside the transform push/pop, a `z-index: 5` overlay covering a ring, a
stacking context's own ring under its positive child, a collapsed-border table's inset ring over its
borders and under its raised cell content, scope predicate theory) and two `TaggedPdfPaintOrderTests`
(artifact after the owner's marked content closes; never nested in a BDC when drawn ahead of a raised
child). Full net8.0 suite green (13478), and diff coverage is 100%. Rendering all 155 showcases
before and after and diffing rasters changed only `outline.pdf` pages 2–3, where the ring now
covers the neighbouring text. PDFium and MuPDF agree.
