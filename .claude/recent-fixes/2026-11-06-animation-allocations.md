# What a frame of animation allocates, and why the garbage collector ran

**Method.** `AnimationBench` reports the allocation of each step and each repaint (`allocated per frame`), and with `PEACH_ANIM_ALLOC=1` the types allocated (an `EventListener` on the runtime's
`GCAllocationTick`; the listener itself shows up as `MoreEventInfo`/`EventWrittenEventArgs`, and slows the run, so read fps from a run without it). `dotnet-trace collect --profile
dotnet-sampled-thread-time,gc-collect` + `dotnet-trace convert --format speedscope` and a small script over the events found who called what (the parents of `GC.RemoveMemoryPressure`, the
callers of CsWinRT's `FromAbi`).

**Found.**
- The step of 120 animated boxes allocated 2 MB a frame, half of it `Token[]`: `CssValueInterpolator.Scan` tokenized the same keyframe strings every frame. It now keeps the token lists per
  parser (`ScanCache`, 1024 entries, the lists are never changed by readers): 1.9 MB to 0.9 MB, a showcase total of 1642 MB to 835 MB in six seconds.
- Every `gen2` collection in the layout showcase is `InducedNotForced`: the finalizer runs `WinRT.IObjectReference.Finalize` -> `GC.RemoveMemoryPressure`, so each WinRT wrapper made per frame
  (bitmaps, geometries, layers) adds to the pressure that makes the runtime collect. The creators, by sampled time: `BitmapFor.Make` (text bitmaps), `AddLayer`, `BeginSurfaceClip`,
  `BuildGeometry`, `CreateRetainedLayer`.
- Text that moves each frame (margin, flex, padding animation) was rasterized and uploaded again for each of 64 positions within a pixel it passed through (about 1000 masks and 1400 bitmaps every
  two seconds, 19% of a core). `Win2dRenderContext.TextPositionSteps` (new, public; the phase count is in the mask key, so masks of two settings never mix) lets `HtmlView` keep text to
  quarter-pixel steps while it plays: the layout page without the font-size card went from 11 to 3 frames under 60 fps in six seconds, repaint allocation from 790 KB to 250 KB a frame.

**Not fixed, measured.** With the `font-size` card playing, 14-16 of 650 frames (2.3%) are under 60 fps, each one a `gen2` that pauses 11 ms (3 ms without that card); lowering the three text
cache budgets by 6x and 24x changed nothing, so it is not the size of those caches. Every frame there has a new font size, so every run is shaped, hinted and rasterized again, and the
likely cost is the per-size hinting state of `PeachDrawing.Text`. Next candidates: cache `CanvasGeometry` for repeated rounded-rectangle clips (`BuildGeometry`, `BeginSurfaceClip`),
find what makes a retained layer per frame (`CreateRetainedLayer` is 52 ms of the sample), and cap per-size hinting caches.
The transitions and scroll showcases do nothing until a pointer or scroll drives them, so `--anim-bench` shows `running=False` for them.
