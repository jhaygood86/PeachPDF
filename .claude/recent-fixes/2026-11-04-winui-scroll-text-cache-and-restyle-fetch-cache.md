# WinUI scroll: text mask cache, grayscale while moving; restyle no longer refetches the web

Follow-up to [the region-paint fix](2026-11-04-winui-region-paint-hands-the-painter-the-region.md): scrolling was still choppy and late-painting in a **Debug** build. Not profiled; the changes follow from reading the text path.

**Cause.** On screen every text run took `Win2dCanvas.TryDrawMasked`: shape + hint + CPU-rasterize the run (`Win2dTextMask.Rasterize`), `Flush()` and read the pixels behind it back from the GPU, blend per channel on the CPU, upload a new `CanvasBitmap`. Nothing was cached, so every strip Win2D invalidated while scrolling redid it for every run, synchronously on the UI thread (the compositor scrolls ahead of it, hence the blank tiles).

**What changed.**
- `Win2dTextMaskCache` (in `Win2dRenderContext.TextMasks`): coverage masks keyed by text/font/scale/sub-pixel phase (1/64), stored unclipped at a whole-pixel origin and cropped to the visible limit on the way out. Colour and backdrop are not in the key. The cache stores what a quantized origin produces, so output can differ from the uncached path by under 1/128 px.
- (The "grayscale while scrolling" switch and its idle timer that this note first described were removed: the timer repainted the whole view between scroll events and made scrolling far worse; see [the GPU text blend](2026-11-04-gpu-cleartype-text-blend.md).)
- `PEACH_TEXT_STATS=1` prints per-stage text timings (`Win2dTextStatistics`).
- The overlay (`CanvasControl` for fixed/sticky boxes) is redrawn per scroll step only if a fixed/sticky box moved or the view is in viewport mode; `InvalidateRegion` now also invalidates it so a hover change inside a fixed bar shows.

**Restyle.**
- Sibling style sharing treated `:hover/:active/:focus*` as constant, so identical sibling `<li>` could share one hovered/unhovered style once a host had live interaction state. `RuleSharing.UsesInteractionState` now blocks sharing while `HtmlContainerInt.Interaction` is non-null (PDF output, where it is null, keeps the sharing).
- The "state selectors in use" scan missed rules nested in another rule (`.card { &:hover {} }`), so such hovers were silently ignored; it now descends into `NestedRules`.
- `HostResourceLoader` keeps http(s) responses in memory for 5 minutes (<=8 MB each, 64 MB total, not `no-store`/`private`). A hover/focus restyle reparses the page, which fetched every `<link>` stylesheet and `@font-face` again from the network; that was likely most of the multi-second wait on live sites.

**Not done.** The in-place incremental restyle (plan Part B2-B3: scratch cascade of the affected elements, paint-only adoption, relayout without reparse). Findings that shape it: the cascade runs against the corrected box tree and once-only guards skip `::first-line`/`::first-letter`/placeholder; one `HtmlTag` can own several boxes; `DerivedStyle` has no invalidate-all and nothing invalidates `font-size`; screen documents cannot use `TryResumeLayoutAt`. Also not done: GPU-side LCD blend, banded/time-budgeted region painting, prefetch beyond the viewport, `MeasurePaintReach` caching.

**Evidence.** `PeachDrawing.Win2D.Tests` 210 pass (7 new in `TextMaskCacheTests`), `PeachBrackets.Tests` 605 pass (2 new in `InteractionStateTests`), `PeachBrackets.WinUI.UITests` pass. No before/after timings yet: run the WinUI example with `PEACH_TEXT_STATS=1`, Debug and Release.
