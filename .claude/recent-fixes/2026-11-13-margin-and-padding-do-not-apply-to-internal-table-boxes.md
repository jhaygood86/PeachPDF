# `margin` and `padding` applied to internal table boxes

CSS 2.1 §8.3: `margin` applies to all elements *except* table display types other than `table`, `inline-table` and `table-caption`; §8.4: `padding` applies to all elements except `table-row-group`, `table-header-group`, `table-footer-group`, `table-row`, `table-column-group` and `table-column`. The engine applied both to every box, so `display: table-row-group; margin: 50px` (the css2 `*-applies-to` tests) pushed the group around.

`CssBox.MarginApplies`/`PaddingApplies` name the two sets; the four used margins (`ActualMarginTop`/`Bottom` and `CssLayoutEngine.GetActualMarginLeft`/`Right`) and `DerivedStyle.ResolvePadding` answer 0 for a box they do not apply to (a cell keeps its padding but not its margin).

css2-flow `margin-padding-clear` 544 -> 567 passes. Remaining `*-applies-to-*` failures (`table-column`, `table-caption`, `inline-table` variants, text-indent, white-space...) were not looked at.
