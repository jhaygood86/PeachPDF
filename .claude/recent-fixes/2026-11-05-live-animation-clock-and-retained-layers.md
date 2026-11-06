# Live animation clock, retained layers and animated images

**What it is.** `HtmlDocument.AdvanceAnimations(now)` (behind `HtmlDocumentOptions.AnimationsEnabled`) plays CSS animations, transitions and animated images; `PeachDrawing.Core` gained
`Canvas.CreateRetainedLayer` / `RetainedLayer` and `Image.FrameCount/GetFrameDuration/LoopCount/CurrentFrame`; `Win2dCanvas` keeps a retained layer as a render-target texture;
`PeachBrackets.WinUI.HtmlView.AnimationsEnabled` runs the clock on `CompositionTarget.Rendering` only while something plays. Limits: [the accepted gaps](../accepted-gaps/css-animation-live-playback-limitations.md).

**Load-bearing ideas.**
- The still-frame path (`AnimationProgress`, `AnimationApplier.Apply` in the cascade) is untouched; the clock is a second consumer of the same sampler (`Sample` split into `BuildFrames` +
  `SampleFrames`, `AnimationTimeline.LiveProgress` for delay/fill/direction on an absolute time). `AnimationsEnabled` only collects the keyframes at cascade time; nothing is applied by the cascade.
- A frame writes the sampled value onto the box with `CssPropertyRegistry.TrySet` and `InteractionRestyler.InvalidatePaintCaches`, the same way the hover restyle does. The clock follows
  a reload by keying runs on the element's `SourceOrdinal` + animation name, and an in-place restyle by comparing `box.ComputedStyle` with the style it left (a different one means the cascade
  rebuilt the box, so the base values are read again).
- Transitions start from `InteractionRestyler.Restyle` (`Classify` now also returns the changed `(property, old, new)` list) via `AnimationClock.BeginTransitions`; `transition-*` got `html`
  bindings in `css-properties.json` so `CssBox.TransitionProperty` etc. exist.
- The painter promotes a box that animates `transform`/`opacity` and has none inside it (`FragmentPainter.TryPaintRetained`): it records `PaintTagged` into a `RetainedLayer` and draws it with
  `ActualOpacity` under the transform the caller already pushed. Layers are invalid when `AnimationClock.ContentGeneration` changes (a restyle or reload).

**Found by running it (not by reading).**
- The default `TileRetainedLayer` recorder must push a clip equal to the layer's bounds: the painter culls against `g.GetClip()`, which for a tile is its own origin-based rectangle, so a box
  not at the page origin recorded nothing (a box 50pt down vanished).
- A test that asserted `R == 255` on a page that never loaded its image passed: white has R 255 too. A `RasterRenderContext` needs a `ResourceLoader` for `data:` images. Assert on a colour that
  only the content can produce.
- The engine's own dirty regions (`StyleUpdater.DirtyRegionsForExtents`) walk the whole fragment tree and parse `box-shadow` per call; the clock computes each box's rectangle from its cached
  subtree extent and matrix (`AnimationClock.AddDirtyRegions`), once per layout.
- **`CanvasVirtualControl` hands back invalidated rectangles cut into bands**: eight small rectangles arrived as 16-26 regions, each paying for a drawing session (about 0.3 ms) and a
  document paint. Two changes, both in `HtmlView`: `PaintRegions` paints each cluster of regions once into the shared render target and every region copies its slice
  (`HtmlViewLogic.ClusterRegions`), and an animation frame invalidates one rectangle per page. The keyframes showcase went from 43 fps (p50 repaint 19.5 ms) to 127 fps (4.7 ms).
- A new image arriving does not tell the box tree; `CssRectImage.Image`'s setter bumps `HtmlContainerInt.ImageGeneration` and the clock (and `HtmlDocument.HasRunningAnimations`) rescans for
  animated images, and the view re-arms its frame loop after each paint, because an `<img>` has its image only once it has been painted.

**Measured** (Release, `--anim-bench`, 144 Hz display, hardware adapter): keyframes 127 fps (interval p95 13.5 ms, repaint p50 4.7 ms: blurred card shadows repainted behind each box), 120 boxes
142 fps (interval p95 7.9 ms, repaint p50 1.1 ms), animated GIF 141 fps (p95 7.4 ms). Nothing under 60 fps. Gen2 collections during the keyframes run (65 in 6 s) cost the occasional one-vsync miss.

**Not done.** Painting animated boxes on a surface above the page (would remove the repaint behind them; needs overlap testing); see the accepted gaps.
