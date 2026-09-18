# `FinalizeFlowBoxExit`'s declared-height extension is for a nested atomic inline, never the flow root

PR #1175 ("Fix inline-flowed inline-block declared height") pointed the "extend `MaxBottom`" branch of
`CssLayoutEngine.FinalizeFlowBoxExit` at `ResolveAtomicInlineDeclaredHeight(box)` instead of
`box.ActualHeight`, so an inline-flowed inline-block's line is sized by its declared `height`/`min-height`.
That is right for the box it was written for, but `CreateLineBoxes` flows **every** block, grid item, flex
item and table cell that holds inline content as `FlowBox(blockBox, blockBox)`, so the same branch also ran
for the flow root itself: `opensHere` is true, and a block/table-cell display passes the `is not
Keywords.Inline` guard.

## What went wrong

For the root, `startY` is the **content** top, and `ResolveAtomicInlineDeclaredHeight` answers in
**border-box** terms - and it answers for every box, because `min-height`'s initial `0` is itself a valid
length, so `declared` was at least the vertical padding + border `P` even with nothing declared. The
comparison `MaxBottom - startY < declared` therefore fired for any padded single-line block, and then
`ActualBottom = MaxBottom + padBottom + borderBottom` added the bottom inset again. Net height:
`P + max(content, declared)` instead of `max(content, declared) + P`.

Measured on the v0.9.19 showcases (Chrome in brackets): `.cell{padding:10pt;font-size:10pt}` 31.5 -> 40.0
(31.5); a content-box `min-height:40pt` card with 10pt padding 60 -> 80 (60); the `text_transform` boxes
28.9 -> 42.4; table rows 22.3 -> 23.5.

Before #1175 the same compare was dead for a block: `ActualBottom` had just been set to `Location.Y`, so
`ActualHeight` was 0. An explicit `height` hid the regression from the existing grid/flex tests (all of
them used `height:20pt`), because `ApplyHeight` overwrites the block's height after the flow.

## Fix

One guard: `box != blockBox &&` on that branch. The root's own declared height is applied by
`ApplyHeight`/`GetBoxHeight` (`min-height`, `max-height`, the table cell's own maximum), so only a NESTED
atomic inline is compared in `FinalizeFlowBoxExit`. This restores the pre-#1175 behaviour exactly for a
root block. See
[the invariant](../invariants/layout-finalizeflowboxexit-runs-for-the-flow-root-itself.md).

## Deliberately not done

Skipping `MinHeight == "0"` inside `ResolveAtomicInlineDeclaredHeight` would also have stopped the padded
one-liner, but it would have removed the padding floor an inline-block button relies on when its vertical
padding exceeds one small-font line - and it would have left the root-block case wrong for a real
`min-height`. The bug was the caller comparing the wrong box, not the resolver.

## Evidence

`BlockLevelPaddingSingleLineHeightTests` (14 cases: plain, bordered, border-box, content-box and
border-box `min-height`, two-line content, grid items and track, table cells incl. the `height:30pt`
row, row/column flex items, a tall inline-block inside a padded block, repeated layout). Every expectation
is relative to an unpadded twin in the same document, so no font metric is hard-coded. On unmodified `main`
13 of 14 failed with exactly the numbers above (32.75 expected vs 40 actual, 60 vs 80, 46 vs 62); with the
guard all 14 pass, as do the ~1,800 grid/flex/table/inline-block/height/baseline/layout tests run
alongside them.
