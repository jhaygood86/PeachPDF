# font-size in container-relative units on a box with text (#619)

`font-size: 10cqw` gave a box with text content a font of 0 (invisible text). Word splitting runs during cascade and reads
`ActualFont`, which caches its size on first read; at that moment no container has a layout in the pass, so the `cq*` basis was 0
and stayed 0. Viewport units were never affected through the real pipeline (the page size is known before cascade): rendering
`font-size: 5vw` with the CLI gives 22.5pt on a 600px page. The `vw` zeros seen in the test harness came from it setting the page
size after `SetHtml`.

**Fix:** the container-query convergence loop already re-runs `SetHtml` with the previous pass's `ContainerQuerySizes` (it
used them for `@container` rule matching only). `HtmlContainerInt.SetHtml` now keeps them as `PreviousPassContainerSizes`, and
`CssBox.GetContainerRelativeUnitBasis` falls back on the entry for the container's `Id` when the container's live content box is
empty (not laid out yet in this pass). The first pass of a document still sees 0; the loop always runs at least one refinement
pass for a document with a size container, and stops when the sizes agree, so the font that is finally laid out used the settled
sizes. The font is not invalidated, because every pass re-cascades into new boxes.

**Found by running it:** a `calc(1em + 5cqw)` font-size rendered 41pt instead of 26pt in the CLI. The declaring box was right
(26pt) and its anonymous text box measured the inherited calc text against 26pt again. That is the compounding fixed by
`2026-11-01-font-size-lazy-calc-parent-terms-made-absolute.md`, so this change needs that one underneath it for mixed calcs.

**Evidence:** `ContainerRelativeFontSizeTests` (6 cases, all failing before: text box, `cqi`, `calc()` mixes, inherited size, a
narrower container); `ContainerQueryLayoutIntegrationTests` had pinned the old zero and now expects the real size. CLI render of
`10cqw` on a 400px container: 30pt. Engine 16960/16960, `PeachPDF.Tests` and the source-generator tests green.
