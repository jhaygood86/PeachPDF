# WinUI paint counters, and a "no text inside" hint for rounded clips

Found by measuring the Ars Technica showcase (`--scroll-bench web_arstechnica`, Release), not by reading: what hitched while scrolling was repainting newly exposed
strips on the UI thread (20-110 ms each, cost per strip not proportional to its area).

**Counters.** `PEACH_PAINT_STATS=1` (`PEACH_PAINT_STATS_FILE=<path>` appends) writes two lines per paint: `PaintStatistics` (engine: fragments in the page vs entered,
stacking-search and escape-check node visits, paint-reach pre-pass and whole-paint ms) and `Win2dPaintStatistics` (text flushes, clip counts, surface clips and their
end, text-mask lookups, image draw/load). The page-wide walks the code reading suspected were measured and are not the cost (reach 0.1 ms).

**What the counters showed.** A paint on Ars opens ~330 geometry clips; every one became a *surface clip* (a render target copy so ClearType text inside can blend), 76%
of them around content with no text. They come from `PaintStackingParticipant`/`PushClipsFor` re-pushing a rounded `overflow` ancestor's clip around every hoisted
descendant, not from box shadows (a count by caller proved that; the huge exclusion path is not used for them).

**Fixes.**
- `Canvas.PushClipAroundShapes(GraphicsPath)` (virtual, defaults to `PushClip(path)`): the painter uses it when `FragmentPainter.FragmentMayDrawText` proves a subtree
  has no text (every word an image or whitespace, no box under it with a content painter other than a loaded image). Win2D then uses a plain layer. 11,324 -> 5,376 surface clips
  per benchmark; fling painting 290 -> 200 ms, 21 -> 13 ms/Mpx. A wrong "true" costs speed, a wrong "false" costs ClearType, so anything not understood answers true.
- Surface-clip targets are pooled per canvas and copy/composite only the geometry's pixel bounds (per-clip begin cost down ~40%).
- Images Windows decodes are held at the resolution the draw reads (`Win2dImage.ScaleFor`, quarter-octave steps); no measurable effect on Ars, whose images are already
  right-sized by `srcset`, and a Windows-scaled PNG dimmed a 1 px line (a JPEG is scaled in decode, anything else decoded whole then reduced with the cubic filter).

**Traps.**
- Deferring the surface decision to the first draw inside the clip was measured and rejected: no clip was empty, and a first non-text draw can be followed by text.
- A first-draw "scale" hypothesis for images, and "the paint-reach pre-pass is page-wide" were both wrong here; run the counters before optimizing.
- The steady-drag first paints (70-110 ms) did not move: they are text-mask misses, text flushes and first image loads.

**Evidence.** Engine 19,715, PeachBrackets 1,198, PeachPDF 1,922 (+9 skipped), Win2D 299, Core 169 pass (net10.0). Not done: diff-coverage gate, net11.0 run, painting
tiles ahead of the viewport (the larger fix), the remaining ~2,500 text-free-looking surface clips from other call sites.

## Idle painting ahead of the viewport (same day)

`HtmlView` now paints 256 px bands into a scratch render target while it is idle (120 ms after the last scroll step, 400 ms after a load, 40 ms between bands), nearest the
viewport first and at most 16 bands (4,096 px) either side (`HtmlViewLogic.NextWarmBand`, `HtmlView.Warm.cs`), so text masks and image bitmaps are cached before the scroll
reaches them. Steady-drag region painting on Ars went 380 ms -> 127-154 ms (70 -> 23-28 ms/Mpx); wheel and fling were already at their warm cost and did not change.
**Trade-off, measured:** the work is moved, not removed, and a band that holds many images costs up to ~75 ms (512 px bands cost up to 170 ms), so a user who starts
scrolling during a warm can still wait one band. It stops for a document with transparency, viewport-mode backgrounds, a restyle in progress or a lost device.
**Found on the way, not fixed:** in one run with the real mouse over the window, `pointer state` (hover restyle) took 955-1558 ms on the Ars page - a far larger hitch than any
paint; the in-place restyle is evidently not covering it there.

## Hover over an inline SVG icon no longer reloads the page (same day)

The 1-1.5 s `pointer state` hitch was found with the real pointer over the bench window (SetCursorPos sweep) and `PEACH_PAINT_STATS=1`, which now notes why a restyle was not applied in
place: `a:hover .icon`-style rules on the page's 129 inline SVG icons made `InteractionRestyler` give up and reload the document. A `CssBoxSvg` now keeps the children its
scene graph was built from when a state rule styled one of its elements (`_stateBoxes`), and the restyler builds that graphic again (`RebuildForState`, the shape `StepAnimation` already used)
after raising `StyleGeneration`, adding the graphic's rect to the dirty regions. Hover changes on Ars: 860-1,500 ms -> 106-133 ms. Tests: `InteractionStateTests` asserts `Incremental` and dirty regions
for the sibling and descendant forms; Engine 19,715, PeachBrackets 1,200 pass.

## Hover over a card link or image no longer reloads the page either (same day)

The remaining 450-570 ms hovers (`<a>`, `<img>`) were found by naming, in the fallback reason (`PEACH_PAINT_STATS=1`), the style areas and properties that differed: the cascade
run again on one box does not redo what the load did to it afterwards. An `a` that holds a block (an absolutely positioned image gets a block wrapper) was made a block, and read back as
inline; the image was wrapped and left `static`, and read back as `absolute`, with color, line-height and tab-size inherited through the wrapper's defaults. Every such box looked
changed, so every hover over a card on Ars loaded the page again. **Fix (`InteractionRestyler`):** run the cascade under the old state first, then under the new, and let
the state's doing be what differs between those two runs: a box keeps the style it has in every area the two agree on (`StateDelta`). The two text areas hold paint beside layout, so
they are not taken whole: each property that differs is set on the box's own style by name. Measured on Ars with the real pointer: hover change median 450-1,500 ms -> 27 ms (p90 46-108 ms, max ~210 ms), no
fallback in two 100+ hover sweeps. `IncrementalInteractionRestyleTests` checks three corrected boxes against a full reload (two fail without the change) and `InteractionStateTests` an inline-holds-block hover.
**Trap:** the reason only said "a style area that holds more than paint changed" until it was made to name the area and the properties; the `display: block -> inline` in both directions (hover in and out)
is what showed it was no hover's doing. **Not done:** a hover that really changes a layout property still loads the page again, by design.

## Memory: clip surfaces sized to the clip, pooled across paints (same day)

The original complaint was memory. `PEACH_PAINT_STATS` now also writes a `(memory)` line (scratch targets, each text cache, image bitmaps, clip surfaces made); the cheap answer was
an experiment, not a count: with surface clips switched off, a bench run on Ars peaked at ~345 MB private / ~370 MB GPU against ~680 MB / ~550 MB. **Every surface clip was a render target the size of
the whole paint target (1536 x 2560 px = 15.7 MB), allocated and freed per clip: 834 targets, 533 MB, in one bench run** (image bitmaps were only 10 MB for 37 images; text caches 13 MB).
- A clip surface is now as big as the clip's pixel bounds (rounded up to 64 px) and starts at its origin: `Win2dCanvas._deviceBase` is shifted for the clip's duration (everything that places
  drawing reads it through `EffectiveTransform` or the session transform) and restored at the pop; the surface is composited back from its origin to the region's place.
- `Win2dSurfacePool` (per render context, 24 MB budget, oldest out first, counted as in use only while a canvas has one) keeps them between paints: 834 allocations -> 34.
- The LCD text blend's four scratch targets (60 MB at 96 dpi) are sized to the area a flush covers, not the target, and shrink after 48 flushes in a row that need under a quarter.
Result on Ars (Release bench): peak private 680 -> 390 MB, peak GPU dedicated 516-586 -> 426-452 MB; fling/wheel painting unchanged (13-14 ms/Mpx).
**Trap:** tests must not dispose `CanvasDevice.GetSharedDevice()` (two new tests did and broke unrelated ones in the same run).
`GpuTextTests.TextInsideASurfaceClip_IsDrawnWhereItWouldBeWithoutTheClip...` pins the coordinate shift (nested transforms, 96 and 144 dpi; it fails when the base is not shifted).
`PEACH_NO_WARM=1` switches painting ahead off, to tell what it costs from what a page does (+34 MB GPU at idle on Ars). **Not done:** the CanvasVirtualControl surface itself and driver
watermarks (the remaining ~100 MB GPU above the no-clip experiment); the region target (`_regionTarget`, 16 MB) and idle trimming of it.

**Replaced pictures.** `ReplacedFragmentPainter` now gives the clip it pushes around a loaded `<img>` the same no-text hint. It does not move the Ars counts (surface clips 4,854 -> 4,822 per run, 2,111 still holding no text), so
the rest of those are not picture clips; the counters cannot say which painter pushes them (a count by caller is the next thing to add). With warming off the 34 slow cold paints still total about 1.2 s: text masks 27%,
text flushes 20%, surface clips 20%, images 18%. Load time on Ars (layout 1.5 s, cascade 0.6 s, fetch 4.4 s summed over 186 requests) is a separate piece of work from scrolling.
