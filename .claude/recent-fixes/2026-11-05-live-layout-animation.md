# Live animation of layout properties

**What it is.** `AnimationClock.Advance(now, fragmentainers, allowLayout)` writes any `InteractionRestyler.IsLiveAnimatable` property (everything but the structural ones: display, position, float, content,
visibility, custom properties, animation/transition) and reports `LayoutChanged`; `HtmlDocument.AdvanceAnimationsAsync(now, canvas)` then runs the ordinary `LayOutAsync` on the live box tree (no cascade, no
re-parse) and returns the dirty regions of a fragment-tree diff (`AnimationClock.DirtyRegionsForLayout`). `HtmlView` calls it from `CompositionTarget.Rendering`.

**Load-bearing ideas.**
- A box keeps its style-derived caches (`DerivedStyle`: font, resolved lengths) for life, and only the paint ones had an invalidator. A layout-property write therefore replaces the box's `DerivedStyle`
  (`CssBox.ResetDerivedStyle`, which also clears the measured-words flag), instead of chasing each cache.
- An inherited property (font-size, line-height, letter-spacing...) must reach the boxes under the animated one, which resolved their own font eagerly at cascade time. The clock cascades the subtree again
  (`DomParser.RecascadeForState`) and the cascade applies the clock's last values at the animation origin (step 5b) for the boxes it holds (`AnimationClock.ApplyLiveValues`).
- **The cascade hook must only run during the clock's own recascade** (`HasOverrides` is true only then). It first ran for every cascade: a hover restyle then re-applied the old animated value over the
  new rules and a transition's box stayed on its end value. The sibling-style and inherited-only sharing shortcuts in the cascade are off while it runs.
- A first write whose value is what the box already reads back (`background` expanding to longhands) must not count as a layout change: it cost a whole layout for nothing.

**The frame tail.** The layout showcase (every card animating width, height, margin, font-size, padding and flex-grow at once) ran at 57 fps while every frame repainted the whole page, and 94 fps once
the dirty regions came from the diff. Then, found by running it and not by reading it:
- Blurred `box-shadow`s were half the repaint: the same page without them repainted in 2.7 ms (p50) against 6.2 ms. The painter blurred a GPU layer for each card on each repaint. Outer blurred shadows
  are now kept as `RetainedLayer`s keyed by their shape (`ShadowLayerCache`, per document, 64 of them) and drawn translated, so a card that moves or is repainted costs a composite. A card whose size changes
  every frame still misses (its shadow is a different shape each frame); a nine-slice shadow would cover that and was not built. Keyframes repaint p50 went 5.3 to 3.4 ms.
- The region render target of `HtmlView` was made again whenever a repaint region grew, which a growing card does every frame; it now grows in 512 DIP steps and is not made smaller while in use.
- Every second-generation collection in the run was started as `InducedNotForced` (13 in 6 s on both the layout and the keyframes pages, about 16 ms each, none from our code: there is no `GC.Collect` in the
  libraries). Neither `SustainedLowLatency` nor a 64 MB gen0 budget changed the total pause. They come from the platform layer (not traced further) and are the remaining slow frames.
- The benches only mean something with a display that is awake: with it asleep every showcase, including ones this change does not touch, came out at 31 fps (interval p50 31 ms).

**Measured** (Release `--anim-bench`, 144 Hz display awake). Layout showcase 106-109 fps (interval p50 7.3 ms, p95 15.7-17 ms, 21-25 of about 650 frames slower than 60 fps, the induced collections); keyframes 140 fps (9 of 840
frames slower than 60 fps); 120-box stress 142 fps (1); animated GIF 142 fps (2). `PEACH_ANIM_HTML=<file>` plays another file in place of the showcase, `PEACH_ANIM_GC=1` counts what started each gen2.

**Not done.** Subtree relayout; layout properties in hover transitions (the restyle reloads the document for them); structural properties.
