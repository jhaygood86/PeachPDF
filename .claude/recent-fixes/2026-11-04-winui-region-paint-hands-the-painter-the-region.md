# WinUI HtmlView: region repaints hand the painter the region; no blank during a restyle

**Symptom (read from code, not profiled):** scrolling `screen_long_page` / `screen_fixed_toolbar` was slow; hovering the nav bar on the live peachstatetechnologies.com page blanked the view for the length of the restyle.

**Cause 1.** `OnRegionsInvalidated` painted with `PaintPageAsync` onto a `Win2dCanvas.FromSession` whose clip is the whole page. The session's own region clip is invisible to `Canvas.GetClip()`, which `FragmentPainter` culls against, so every fragment of the page was drawn for every invalidated strip and Win2D threw almost all of it away. It now calls `PaintPageRegionAsync` with the region converted to page-local points (this pushes the clip that makes culling work).

**Cause 2.** A hover/focus change restyles by reparsing (see the accepted gap on interaction-state reloads), which drops `FragmentTree`; a region painted meanwhile was cleared and then drew nothing. `HtmlView` now sets `_restyling` around the state calls, `OnRegionsInvalidated` leaves the surface alone while it is set and records `_paintDeferred`, and the pump invalidates everything once the restyle returns.

**Also:** dirty rects from `SetViewport` that overlap are joined before invalidation (`CoalesceRegions`), so a moving fixed box's old and new bleeds are not painted twice.

**Not done:** the reparse per hover is still there (the larger fix is an incremental restyle); `MeasurePaintReach`/`StackingOrder.Flatten` still walk the tree each paint (judged cheap next to drawing the whole page; measure before caching, a cache keyed on fragments must survive `StyleUpdater.Apply`). Nothing here was profiled; compare timings on the three demos before claiming a number.

**Evidence:** `PeachBrackets.WinUI.UITests` 66/66 pass; WinUI project builds with 0 warnings.
