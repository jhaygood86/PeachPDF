# A positioned box inside a non-positioned stacking context keeps an overflow clip it should escape (#1315)

**Gap:** CSS Overflow 3 §3 clips only descendants whose containing block chain passes through the
clipping box. `relative > overflow:hidden > opacity:.5 > absolute` should leave the absolute box unclipped,
because `opacity` forms a stacking context but not a containing block. PeachPDF still clips it to the
`overflow: hidden` box. The same applies to `mix-blend-mode`, a flex or grid item with `z-index` in the
middle position. (`position: fixed` in the same place is already fine.) Blink agrees with the spec: its
paint property tree gives out-of-flow descendants their own clip context, and
`EffectCanUseCurrentClipAsOutputClip` refuses to use the current clip as a group's output clip when an
out-of-flow descendant could escape it.

**Mechanism (measured, 2026-11-01):** the clip reaches the absolute box through **two** pushes, not one:

1. The `opacity` box is itself a stacking participant hoisted past the `overflow: hidden` box, so the
   *outer* stacking context paints it through `PaintStackingParticipant`, which pushes the overflow box's
   padding clip (`RenderUtils.PushAncestorOverflowClips`) around the opacity box's whole paint call. With
   a real group the opacity box then paints into a layer composited under that clip.
2. The opacity box's own `PaintBoxContent` pushes the same clip again (`fragment.OverflowClip`) around its
   own content and its stacking layers.

The absolute box's own `OverflowClip` is already null (#1314), so neither push is the participant's.

**What was tried:** lifting push 2 for the layers of the box (popping it before `PaintLayer` and re-pushing
it per participant that is on the clipper's chain, via `DomUtils.IsOnClippingChainOf`) is easy and passes
the box-local tests, but changes nothing for the repro, because push 1 is outside the layer and the tile
composite is clipped by it. Fixing push 1 means the group must be composited unclipped while its on-chain
content is clipped *inside* the layer: that needs every clip pushed for a hoisted ancestor to carry the
clipper's identity and to be pushed inside the layer (around on-chain content only), across the vector
layer path, `TryFlatten` and `PaintRasterized`, plus the multi-ancestor case (a participant's own
`OverflowClip` is only the nearest clipper). That is a paint-structure change, not a local one.

**Regression coverage kept:** `OverflowClipIntegrationTests` pins the cases that already behave (clipper is
the containing block; fixed; in-flow child) so a future fix cannot regress them.
