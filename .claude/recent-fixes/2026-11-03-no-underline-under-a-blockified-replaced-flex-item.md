# A decoration is not drawn under a replaced flex or grid item

Once flex/grid items were blockified, an `<svg>` or `<img>` item of a decorated `inline-flex` link stopped being an
atomic inline (`DomUtils.IsAtomicInline` requires `IsInline`), so `DecorationContent.Collect` fell through to the line
rectangle branch and unioned the item's own rectangle into the decoration span: an extra underline segment under the
item. `Collect` now returns for any replaced box (`MonolithicContent.IsReplaced`), inline or not: a replaced element has
no inline content for the line to land on. Text inside a non-replaced flex item is still decorated.
Evidence: `InlineFlexUnderline_IsNotDrawnUnderAReplacedFlexItem` (fails on main with two lines, one under the item).
