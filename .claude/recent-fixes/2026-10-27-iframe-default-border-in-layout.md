# An iframe's 2px default border takes part in layout (#1803)

The border was set in `CssBoxFrame`'s constructor, which the cascade then discarded, so layout saw width 0 while an old paint path still drew it. It is now a UA rule in `CssDefaults` (`iframe { border: 2px solid ... }`), so `border:0` removes it and a bare iframe is 228 x 115.5pt like Chrome.

Two double counts surfaced once the border was real, both specific to a blockified frame (flex/grid item):
- `CssLayoutEngineGrid` pinned the width from `GetFitContentWidth`, whose replaced-item value already includes the item's own border/padding, but the pin is content-space. The own padding+border is now subtracted for replaced natural-width items.
- `CssLayoutEngine.MeasureIntrinsicSize` adds border+padding to `word.Height` (right for an inline replaced box, which is its own border box). A blockified `CssBoxFrame` gets them again from block layout, so it is skipped there. Do not widen this to `CssBoxImage`: `FlexboxIntegrationTests.StretchedBorderBoxColumnItem_WithPadding_FillsLineCrossSize` fails.

Three `FlexGridItemBlockificationIntegrationTests` encoded the old 225 x 112.5 and now expect 228 x 115.5.
