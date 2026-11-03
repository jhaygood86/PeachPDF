# Automatic minimum size for column flex items and bare `fr` grid tracks (#1916)

**Column flex.** `ResolveAutoMinimums` (CssLayoutEngineFlex) used to return at once for a column. It now measures the line first
(a line that does not overflow is untouched) and `ResolveColumnAutoMinimums` gives each item of an overflowing line
`AutoMinMainSize = min(content height, definite height, max-height, hypothetical)`; the existing `ShrinkAgainstAutoMinimums` freeze loop
then does the §9.7 arithmetic. The content height is `NaturalMainSize` when the hypothetical size came from auto layout
(no `height`, no `flex-basis`, no grow); otherwise the item is laid out once with `height: auto` to learn it and then laid out again at its
hypothetical size through `ResizeItem`, which is what `MeasureItem` left. `min-height` was already `auto` (the old gap note was stale on that:
the initial value changed with the row fix), so an authored `min-height: 0` is the only opt-out besides scroll containers and `flex-shrink: 0`.
Replaced items are skipped: a pinned test (`StretchedBorderBoxColumnItem_WithPadding_FillsLineCrossSize`) has an image whose
aspect-ratio height from its stretched width exceeds the column, and the specification gives it a transferred minimum of its own.

**Grid.** A bare `fr` track is `minmax(auto, fr)`, but `InitTrack` gave it base 0, so only an explicit `minmax(auto, 1fr)` floored it. `Flex`
now takes the items' automatic minimum contribution as its base (`AutoMinimumOf`: 0 for a scroll container, an authored `min-width`, else the
smaller of min-content and a definite width); `FindFlexFraction` already treated bases as floors. `MeasureColumnIntrinsics` and the grid's own
intrinsic walk measure `fr` tracks too. `minmax(0, 1fr)` opts out.

**Found by running.** Old pin `AColumnFlexItemLaysOutTheSameAsWithAnExplicitZeroMinimum` asserted the old behaviour on purpose and was
rewritten. A scroll container (`overflow: hidden`) item of an auto-height column keeps its content height with or without this change
(some other path), so no test pins it as shrinking. A sweep of all 233 showcases against the previous build showed no rendering change; the new
`automatic_minimum_size` showcase was checked in PDFium and MuPDF.

**Not done.** See the accepted gap (grow case, replaced column items, spanning items, auto tracks' exemptions).
