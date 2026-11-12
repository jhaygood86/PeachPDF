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
**Still reloading on Ars (400-570 ms):** hover changes on `<a>` and `<img>` whose new style differs in an area that is not paint-only; the reason now names the areas
("content, font..."), but the pointer sweep stopped delivering events before it could be read. **Not done:** a graphic inside a recascaded root is not rebuilt for a changed `currentColor`
by this path (unchanged behavior).
