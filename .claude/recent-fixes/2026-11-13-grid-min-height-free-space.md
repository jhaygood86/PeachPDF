# Grid min-height gives its free space to flexible rows (#2402)

An auto-height horizontal grid taller than its rows only because of `min-height` laid its `fr` rows out at content size, so
`grid-template-rows: auto 1fr auto; min-height: 200mm` left the last row directly under the first while the box itself was 200mm tall.
The column flex case in the same issue was already fixed on main (`growIntoMinHeight` in `CssLayoutEngineFlex`); only tests were added.

The fix is in `CssLayoutEngineGrid`: with no definite height, a resolvable non-percentage `min-height` is passed to `ExpandFlexibleRows` as the
available space (CSS Grid 1 section 12.1). Rows hold their content size as the base, so a short minimum changes nothing. Auto-row stretch under
`min-height` and vertical writing modes are not handled.

Tests: `FlexColumnMinHeightPagedTests` (paged path through `PdfGeneratorLayoutHarness`; the unpaged harness did not show the grid bug).
Verified with the CLI: BOTTOM moved from 13.9mm to 206.0mm.
