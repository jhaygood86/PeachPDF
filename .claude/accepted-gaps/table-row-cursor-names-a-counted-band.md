# The table row loop's band is counted, not reached

`CssLayoutEngineTable.LayoutCells` tracks the band it is filling as a counter (`TableRowCursor.SlotIndex`,
advanced once per break it takes), so it names the band the loop last *opened* rather than the band the row
cursor has reached. Where a row turns out taller than `EstimateRowHeight` predicted — see
[the estimate's own gap](table-pre-checks-decide-from-an-estimate.md) — the two diverge, and
`CalculatePageBreakOffset` then returns a **negative** offset: the rows after a row taller than a page band
are placed back inside it and painted over its content (measured at 1141pt of overlap for a 1400pt row on a
260pt band). Tracked as [issue #432](https://github.com/jhaygood86/PeachPDF/issues/432), pinned by
`TableRowCursorBandTests`.

**Deriving the band from the cursor is not the correction it looks like, and this is the part worth not
re-deriving.** The stale counter is what compensates for the estimate's undershoot: once the loop believes
it is on band `k` it keeps measuring every later row against band `k`'s bottom, so a row that overflowed is
noticed one row late rather than never. A cursor that re-derives its band per row finds every row
comfortably inside a *fresh* band and stops breaking at all — measured as a 40-row 1400pt table on 842pt
pages recording no break anywhere, a repeating `<thead>` appearing on one page instead of five, and four
existing tests failing.

So the estimate has to go first, or the row loop has to ask the question of the row's *real* height, which
is only knowable after the row has been laid out. The second is
[#390](https://github.com/jhaygood86/PeachPDF/issues/390) stage 4's own shape: a row loop that can stop and
record where it stopped can also place a row, see that it straddled, and take the break there instead of
predicting it.
