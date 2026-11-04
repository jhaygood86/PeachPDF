# `MediaType.Screen` is one continuous surface, and fixed, sticky and fixed backgrounds follow the viewport

The `HtmlView` controls laid a widget out as a paged document: a 14000pt "page" (`PageHeightPoints`), pages stacked with a gap, `@page` and page breaks live, `vh` and `height: 100%` resolving against the invented page. `HtmlDocumentOptions.Media` (a string) is now `MediaType { Print, Screen }`; `Screen` is the continuous medium.

**What was load-bearing**

- One enum drives both `@media` matching (`HtmlContainerInt.MediaType`, with `Media` a derived string) and the layout mode (`HtmlContainerInt.Continuous`). PDF is always `Print`: `PdfGenerateConfig.Media` and the CLI's `--media` are gone.
- A continuous document is laid out against **one very large finite fragmentation band** (`ContinuousBandPoints`, 1,000,000pt) so the whole page-grid machinery (`HasRealPageGrid`) keeps working unchanged and nothing can overflow and break; after layout `SettleContinuousSurface` re-finishes the single fragmentainer at its real height, `max(content, viewport)`. See the invariant on never using `double.MaxValue`.
- Page-only constructs are made inert at their one entry each: `@page` is not parsed (`PageRules` stays empty), `BreakPropagation.ForcedBreakBeforeAt/AfterAt` return null for the **page** context only (a multicol still breaks), the `page` property cannot force a break, and `CssUtils.SetPropertyValue` drops `float: footnote|top|bottom|...` and `position: running()` declarations as a browser's cascade does.
- `ViewportHeight` is pinned by the host (never content-derived, or `100vh` content never settles) and feeds `vh` (`GetViewportUnitBasis`), the ICB and fixed boxes' percentage basis (`HtmlContainerInt.FirstPageBandHeight`) and the `height`/`aspect-ratio` media features. Unknown means `auto`.
- **Viewport state** (`Html/Core/ViewportState.cs`): the fragment tree stays immutable; a table of displacements per box (fixed = viewport origin, sticky = the section 3.4 clamp against the viewport and the parent fragment) is applied at paint (`FragmentPainter.PaintFragment` pushes a translation and enters the placement map like a transform) and by the geometry readers (`TextRunCollector`, the link and form-field collectors). `HtmlDocument.SetViewport` returns the regions to repaint (old and new rectangle of every displaced box, the whole box of a fixed background).

**Found by running it**

- Fixed boxes ignored `right` and `bottom` (placement only read `left`/`top`), in print too; they now measure from the viewport's / page area's far edges.
- Tests written for paged screen documents read pixels at positions the new, content-sized surface does not have: they either pass `ViewportHeightPoints` or stay `Print`. `TestDocument.LoadAsync` maps `@page { margin: 0 }` to zero margins for screen documents.
- A stdout redirect nobody reads freezes the demo (the Debug build writes a trace per layout): drain it when launching the app for a screenshot.
- The Windows App SDK `TextBox` cannot be created in a code-only app on every system (the `LimitedAccessFeatures` class factory), so the demo uses lists and combo boxes.
- The default SDK may be the .NET 11 RC, which lacks the Android workload: build the Android projects with a `global.json` pinning 10.0.4xx from another directory.

- **A sticky box drawn into the scrolled canvas jumped**: the `ScrollViewer` moves the canvas on the compositor before `ViewChanged` can repaint anything, so a pinned header scrolled away and snapped back a frame later. Fixed and stuck boxes are now drawn on a non-scrolling overlay (`DocumentPaintLayer.ViewportAttached`; the scrolled canvas paints `Content`), so there is nothing to move. `Content` plus `ViewportAttached` equals `All` pixel for pixel (a test). A sticky table cell is held inside its table, not its row, or a sticky `th` could not leave its row.

- **Links in a pinned bar had no pointer and did nothing**: hit tests (`CursorQueries`, `StyleUpdater.ElementAt`, element and accessibility bounds) ignored the viewport displacement, and tested boxes in source order, so the content after a bar stuck to the top of the viewport (drawn under it) won the hit. They now add `ViewportState.DisplacementOf` and try attached boxes first. The placement map is also not used for the displacement: it is written once per box and read later, so a link that was painted at an earlier scroll position was found where it used to be. The demo swallowed every `LinkClicked` (it cancelled web links, and `Cancel` also stops an in-page anchor from scrolling); it now cancels only addresses.

- **A jump link scrolled the target under the sticky bar**: the view scrolled to the element's top, so the heading sat behind the bar and it looked as if nothing had scrolled to it. `scroll-margin-top` and `scroll-padding-top` (the root's) are now properties, read by `HtmlDocument.GetScrollOffset`, which the WinUI and Android views use for an in-page link. Only the top edge: the other sides and the shorthands are not supported.

**Not done**

- Fixed boxes are anchored to the page area's corner (inside `DocumentMargin`), not the viewport edge.
- A displaced box's overflowing descendants and shadows are repainted through a fixed 24pt bleed on the dirty region.
- A fixed box with a negative `z-index` is drawn above the content, since the attached layer is always on top.
- Sticky inside a nested scroller that really scrolls: the engine has no nested scrolling, so such a box does not stick.

**Evidence**: `ContinuousSurfaceTests` and `ViewportTests` (PeachBrackets.Tests), the WinUI UI tests, `--smoke-all` over all 245 showcases in a real `HtmlView`, and the Android demo on an emulator (`pages=1`, content 81530pt).
