# A clipping multi-column container fills columns of its own height, and words above the band are claimed (#1818, #1896, #1897)

**Height cap (#1818).** `CssLayoutEngineColumns.Layout` took the page's remaining space as the column budget, so a container with `overflow: hidden` and a definite `height` balanced its columns until every word fit and the clip then hid lines from the middle of the text. The budget is now also capped by the container's definite content height (`CssBox.ResolveDefiniteHeightPt`, now `internal`), only for the fragment that starts the container and only when it clips. What the cap leaves over is dropped (clipped), not carried to the next page. Measured: the issue's document gave columns `w1-6 / w17-22 / w33-38`; it now gives `w1-5 / w6-10 / w11-16`.

**Not done: text-only containers (#1849).** The parser still declines to wrap inline-only content of a height-bounded scroll container for columns. Lifting it would route such a monolithic scroll container taller than a page through the columns engine, which fragments it, and `ScrollContainerEstablishingColumnsButHoldingOnlyInlineContent_IsStillSuppressed` pins the one-pass behaviour. The accepted gap stays.

**Words above the band (#1896, #1897).** A `float: none` block with a large negative `margin-top` pulls the text after it above the container, here to document Y -95..-5, above the first page. No column's region reaches there, so `FragmentEmitter.IsOverflowNoColumnClaims` skipped the words (its band test excluded `rect.Top < Region.Top`) and nothing drew them. Confirmed by listing the unclaimed words with their positions (7 of 123 and 1 of 4). The first holder now claims a word that lies wholly above the band on the first page (`IsAboveTheFirstBand`). First page only: on a later page those words lie in an earlier page's band, which claims them itself, so claiming them here would draw them twice.

**Not fixed: #1891.** Enabling `ShiftEmptyLineBelowCrowdingFloats` for a `nowrap` block (both `blockBoxPermitsWrap` and the `nowrap`/`pre` guards) was tried: with `columns:3; width:650px` and a 280px right float after `<br>`, the float and the following line moved to the next page and the float landed at X -48. The deferral of a shifted line is the real work; the accepted gap stays.

**#1850** already passes at `main` (the post-wrap shift from #1743); no change.

Evidence: `PeachBrackets.Engine.Tests` 16209/16209 on net8.0 (Debug).
