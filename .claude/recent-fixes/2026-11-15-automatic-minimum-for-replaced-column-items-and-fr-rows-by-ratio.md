# A replaced column item floors at its natural height, and `fr` rows of an auto-height grid are in ratio

Closes the last two items of the retired "automatic minimum size is not applied to every case" accepted gap (#2174).

**Replaced item in a column flex container (css-flexbox-1 §4.5).** `ResolveColumnAutoMinimums` skipped every image (`if (box.IsImage) continue;`), so an
`<img>` in an overflowing column shrank to nothing. The floor is now `ReplacedContentSuggestionAsync`: the smaller of the content size suggestion (the
natural height plus padding and border) and, when the author gave a definite non-percentage `width`, the transferred size suggestion (that width, less
padding and border, over the aspect ratio, plus padding and border). A stretched width is not definite, so a stretched picture only has the first.
- **Trap, found by running it.** The natural height must not come from `FlexItem.NaturalMainSize` (the height the stretched width gave) nor from
  re-laying the item out and reading `ActualBoxSizingHeight`: for an image the word's height already carries the padding in some states and the engine
  then added it again, giving a floor of 124pt for a border-box picture that should floor at 80pt (the pinned `StretchedBorderBoxColumnItem_WithPadding_FillsLineCrossSize`
  caught it). `ReplacedNaturalSize` now carries `HeightUnits` computed straight from the picture (raster natural size / `srcset` density, svg
  `SvgIntrinsicSize`, the 150px default of an iframe), so no layout runs for the suggestion.
- **Not done.** The `MeasureItem` exemption for a `flex: 1 1 0` image in an auto-height column (`!box.IsImage`) is untouched; it uses the stretched ratio
  height as its floor, which differs from §4.5 only for stretched pictures in that rare shape.

**Flexible rows against an indefinite height (css-grid-1 §12.7.1).** Rows used to be content-sized per row, so `1fr 2fr` over equal content was 10 and 10.
`ExpandFlexibleRowsIndefinite` takes the flex fraction as the largest of each flexible row's size over `max(flex, 1)` and, for an item spanning flexible
rows, `FindFlexFraction` over the rows it crosses with the item's height as the space, then sets each flexible row to `max(content, flex x fraction)`.
- **Spanning items.** `GrowSpannedAutoRows` treated a flexible row as an auto row and grew the last one by the item's overshoot, which made a spanning
  item inflate the rows twice. In the indefinite case an item crossing a flexible row now skips it and only sets the fraction (§12.5 hands such an item to
  the flexible step).
- **`min-height`.** The existing branch that shares a taller `min-height` still runs afterward, over the ratio-sized rows; a short minimum leaves them alone.
- Definite-height grids, vertical grids and row subgrids take the old paths unchanged.

**Evidence.** `FlexColumnReplacedMinimumTests`, `GridFlexibleRowsIndefiniteHeightTests`; Engine suite 20091 and PeachPDF.Tests 2085 pass on net10.0
(`WordSeparatorGlyphTests` is an order-dependent font-metric flake: a different test of that class fails each full run and the class passes alone).
