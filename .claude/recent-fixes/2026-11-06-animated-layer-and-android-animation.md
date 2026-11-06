# Animated paint layer, a Skia retained layer, and animation on the Android view

**What:** `DocumentPaintLayer.Animated` takes a box that only moves or fades out of the surface a host keeps (`Content`), so a frame repaints none of it; `PeachDrawing.Skia` keeps a
`RetainedLayer` as a texture; `PeachBrackets.Android.HtmlView` plays animations (`AnimationsEnabled`, a `Choreographer` loop) and drops only the tiles an animation dirtied; the WinUI view
draws the animated layer in its overlay. The Android demo is resizable, has a two-pane layout from 840 dp, and runs without a touch screen.

**The load-bearing idea:** a box can be drawn later than the page only if nothing painted after it overlaps it and nothing between it and the page changes how it is drawn. So eligibility is
conservative and decided in `AnimationClock.Overlay.cs`: every ancestor is plain (not clipping, not a stacking context), and no other positioned box in the root stacking context with the same or a
higher z-index, and no fixed or sticky box, overlaps the box's *current* transformed bounds. Non-positioned content paints before a stacking-context box (Appendix E step 8), so it never needs testing.
The test runs at every step with the current matrices; a box that changes layers is repainted where it is (`AddDirtyRegions` takes the set difference with the plan the host last painted; after a
layout `HtmlDocument` adds `OverlayFlipRegions`). A box in the plan on both sides of a step contributes no dirty region at all: that is the whole point.

**Found by running it, not by reading:**
- Creating a GPU `SKSurface` for the layer in the middle of a frame (budgeted or not, flushed or not) turned the whole Android emulator frame black, with no exception and with the CPU view fine.
  The layer is recorded into a bitmap and uploaded once (`SKImage.ToTextureImage`); each draw is still a textured quad. Do not "improve" this back to a GPU render target without testing on a device.
- The old tests that asserted `DirtyRegions` was not empty for an opacity animation were asserting the thing this removes; they now assert `OverlayChanged`.
- A positioned box that appears to cover an animated one in the page was not on top in `All` either (the page's own paint order), so the layer test compares `Content` + `Animated` with `All` pixel for
  pixel instead of asserting a colour.

**Not done:** boxes under a clipping or compositing ancestor stay in `Content` (see the accepted gap); `TextPositionSteps` has no Skia equivalent.

**Evidence:** `PeachBrackets.Tests` (`AnimatedLayerTests`: layers vs `All`, no static regions on a move, overlap, clipping ancestor, a box crossed by another), `PeachDrawing.Skia.Tests` (`RetainedLayerTests`), `PeachBrackets.Android.Tests`
(`AnimationLogicTests`), `PeachBrackets.Engine.Tests` 18327 pass; the Android demo on an emulator, GPU and CPU views, phone and a 2400x1600 tablet-size display.
