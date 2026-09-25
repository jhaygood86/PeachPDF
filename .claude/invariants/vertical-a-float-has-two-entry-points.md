# A float in a vertical box reaches layout through two different paths

A change to how a float is placed or wrapped around in a `vertical-rl`/`vertical-lr` box has to cover **both**:

1. **Block path** - `CssBox.LayoutVerticalBlockChildren`, for a box with block-level children. A float child is
   placed by `PlaceVerticalFloat` in child order.
2. **Inline path** - `CssLayoutEngine.CreateVerticalLineBoxes`, for a box whose children are all inline-compatible.
   Since #1038 `DomUtils.ContainsInlinesOnly` counts a float as inline-compatible, so **a box holding only floats (and
   whitespace), or floats among bare text, is on this path**. A float there is collected by
   `MeasureAndCollectWordsInDocumentOrder` and placed by `PlaceFloatsUpTo` at the block-axis position of the column the
   word stream has reached.

Symptom of missing the second: the simplest possible test, one float as the only child of a vertical box, put the float
at the physical *left* (the old horizontal `LayoutBlockChild` placement through `LayoutOutOfFlowDescendants`) while every
test with a paragraph beside it passed.

Two ordering rules on the inline path: a float at word index 0 is placed *before* the first column's span is computed
(or that column ignores it), and one reached mid-stream re-computes the span only if the current column has no words yet
(a column already holding words keeps the room it started with). Floats placed here are skipped by
`LayoutOutOfFlowDescendants` via `VerticalFloatOccupancy`, which is reset when they are collected - a stale value from an
earlier layout would otherwise make it skip a float that was never placed.

See [the fix](../recent-fixes/2026-09-24-vertical-floats-and-clear-are-line-relative.md).
