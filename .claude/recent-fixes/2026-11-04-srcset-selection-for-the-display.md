# srcset images are chosen for the display on screen, and upgraded in place on zoom

**Idea.** Selection is a ranking, not a pick. `ResponsiveImageSelector.Rank` orders every candidate best-first for the
container's policy (`HighestDensity`: density descending, stable, so identical to the old pick; `BestForTarget`: sufficient
ascending, then the rest descending, over-budget last). `Select` returns the first and the rest as `Alternates`, which is what
the load fallback and the re-selection both use. A box's choice is `Choice(Url, Density, EffectiveDensity)`: `Density` is
measured against the `sizes` slot and is what divides the natural size, so layout never depends on which bitmap loaded;
`EffectiveDensity` is measured against the width actually used and is what ranking compares. Do not unify them.

**Re-selection** (`CssBoxImage.ReselectAsync`, driven by `HtmlContainerInt.ReselectImagesAsync`) runs once at the end of
`PerformLayout` and again from `HtmlDocument.UpgradeImagesAsync`. It swaps `_imageWord.Image` only; it never sets
`_wordsSizeMeasured = false`, so there is no relayout and `CssBox` identities (hence `DocumentElement` handles) are unchanged.
It only goes to a candidate whose effective density beats the loaded one at the new width.

**Container, not box.** `DevicePixelRatio`, `RenderScale` (only grows) and `ImageSelection` live on `HtmlContainerInt`, because a
container-query rebuild (`PerformLayout` calls `SetHtml` again) makes new boxes that must choose with the same inputs.

**Traps found by running it.**
- A new `ImageLoadHandler` per swap: reusing one would dispose the stream the cached `Image` of the old URL reads from. The old
  handler is retired (kept) unless `TryEvictImage` finds no other `<img>` showing the old source, in which case the cache entry is
  dropped and the handler disposed. The `Image` is deliberately not disposed on eviction, since a `background-image` of the same
  URL shares the cache and holds it.
- `LayoutHarness` builds `HtmlRenderContext`; to test `SupportsImageType` it is now `internal class` (not sealed) and the harness
  takes `createRenderContext`.
- `PdfGenerator.ToDocumentOptions` sets `HighestDensity` explicitly, so a PDF never depends on a medium default.

**Not done.** Decoding at the display size (`DecoderOptions.TargetSize`) for JPEG: neither Win2D nor Skia pass it, so there is
nothing to measure through them yet. `image-set()`: see the accepted gap. A screen showcase with DPR/zoom variants: the showcase
helper renders through `PdfGenerator`, which pins `HighestDensity`, so only the print side is shown; the screen behaviour is
covered by `ResponsiveImageSelectionTests`.

**Evidence.** `ResponsiveImageTests` (the 14 original tests) and `ResponsiveImageSelectionTests` pass on net8.0.
`ScreenDocument_LoadsFewerPixelsThanHighestDensity`: twenty images offering 100, 200 and 400 pixel-wide candidates decode
100,000 pixels in total on a 1x screen document and 1,600,000 for print.
