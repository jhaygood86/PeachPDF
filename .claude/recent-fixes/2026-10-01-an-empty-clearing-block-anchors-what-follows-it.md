# An empty block that clears a float anchors what follows it (#1535)

`<div style="clear: both"></div>` (the clearfix) placed the content after it beside the float, not below. With text, a
height, or `overflow: hidden` the same div cleared correctly.

**Cause.** `ClearBox` did give the empty div its clearance (it sat at the float's bottom edge), but the next sibling's
position comes from `FoldMarginsPrecedingChild`, which walks back over every previous sibling that
`IsMarginCollapseThrough()` and anchors on the first one that does not. An empty zero-height block collapses
through, so the walk skipped the cleared div and anchored on the block before it, putting the paragraph level with
the float's top.

**Fix.** `CssBox.HasClearance`, set by `ClearBox` whenever clearance actually moved the box (its float-bottom
position is below where it would have been, so a `clear` with nothing to clear is unaffected), and
`IsMarginCollapseThrough` answers false for it. CSS 2.1 §9.5.2 gives clearance as a position constraint and §8.3.1
excludes a box with clearance from the adjoining margins; the existing forced-break rule
(`PlacedByForcedBreak`, "a positional constraint rather than a margin") is the same idea and uses the same walk.

**Not done.** The issue also notes the clearing div being drawn beside a float that had moved to the next page.
That is the same anchor seen across a page boundary and was not separately checked. The documented gap that margin
collapsing ignores clearance in the adjoining set
([accepted gap](../accepted-gaps/margin-collapsing-ignores-clearance.md)) is untouched: this fixes where a cleared
box anchors its successor, not which margins join its set.

Evidence: `FloatPlacementTests.EmptyBlockWithClear_ClearsTheFloatAboveIt` (the issue's four rows: empty, text,
`height: 1pt`, `overflow: hidden`); the empty row failed with `after` at y=32 instead of 132; full net8.0 suite
(14839 tests) passes.
