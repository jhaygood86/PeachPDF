# Animated layer: ancestors, overlap, fixed/sticky and negative z-index

**What:** `DocumentPaintLayer.Animated` no longer requires a plain chain of ancestors, nothing overlapping, and a non-negative z-index. The plan (`AnimationClock.Overlay.cs`) is now a
set of *members*, each a participant of the root stacking context (what `StackingOrder.Flatten` yields), and the animated pass paints each as `PaintStackingParticipant` does for a hoisted box.

**The load-bearing idea:** the animated layer sits over the content layer, so a box can be drawn there only if everything the page paints over it is drawn there too, in the page's order.
- A box under clipping ancestors: the participant's `ClipAncestors` re-apply the clips (already what hoisting did).
- A box inside a stacking context (opacity, filter, transform): the *outermost* stacking context is the member and the whole group is painted by the animated layer. Painting only the
  box under the ancestor's opacity is not equal (the ancestor's own background composites differently), which a pixel comparison of Content + Animated against All shows.
- A later positioned box that overlaps a member joins it as a companion (closure over "paints later and overlaps", capped); an earlier one stays under it in the content layer.
- A fixed/sticky box is drawn by the attached layer over everything, so it is fine when it is meant to be over the member and forces the content layer when it is meant to be under it.
  Only boxes the viewport really holds count (`View.IsAttached`): with no viewport set, a fixed box is an ordinary positioned box and is a companion candidate (it is not eligible, so the box falls back).
- A negative z-index member is drawn apart only when no in-flow ink (text, backgrounds, borders, replaced content) overlaps it.

**Found by running it:**
- `HasPaintExtras` is true for plain boxes (html, body), so it cannot be the "paints something" test; use shadow bleed.
- The stacking-context-ness of an animated box changes with its opacity/transform from step to step (opacity 1 is not a stacking context), so the cached structure is keyed on that too.
- Content + Animated + ViewportAttached is *not* All when a fixed box must sit under a positioned one: the attached layer is always on top. That is the viewport layer's limit, not the animated layer's.
- A member that leaves the plan (or joins it) that is not itself an animated box has no `BoxState` extents, so its last known rectangle is kept in `_knownStatic` for the flip repaint.

**Not done:** a group that blends or uses `backdrop-filter` (it would blend against the content layer, which is not under it in the animated layer); a group that holds a fixed/sticky box.

**Evidence:** `PeachBrackets.Tests` `AnimatedLayerTests` (each case compares the layers with `All` pixel for pixel), the full `PeachBrackets.Tests` and `PeachBrackets.Engine.Tests` suites.
