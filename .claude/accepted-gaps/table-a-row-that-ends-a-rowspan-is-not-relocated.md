# A row that ends a rowspan is left straddling rather than moved

`CssLayoutEngineTable.StraddleCorrectionAppliesTo` declines to move a row when
`TableRowCursor.RowSpannedBoxes` holds an entry keyed to that row — the row a `rowspan` begun earlier
*ends* on. It is left where it falls, and where that crosses a page boundary it is drawn cut through by
it. Measured: a 120pt row ending a two-row span at Y 214 on a 260pt band, bottom 334, against a next band
that would hold it four times over. [Issue #511](https://github.com/jhaygood86/PeachPDF/issues/511),
stated reader-facing under [Breaks between table rows](../../docs/html-css-support.md#breaks-in-tables).

**The decline is not arbitrary, and this is the part worth not re-deriving.** By the time the row loop can
see that the row straddled, `LayoutBodyRow` has run `ApplyCellVerticalAlignment` over the cells that end
on this row — including the spanning cell, which is a child of a **different, earlier** row, reached
through a `CssSpacingBox` whose `ExtendedBox.ActualBottom` it has already written.
`TableRowCursor.Retract` takes back what this row added to the cursor and `PassRewind.RollBackTo` resets
the row's own boxes; neither restores geometry belonging to a box the row does not own, and
`ApplyCellVerticalAlignment` **deep-offsets a subtree** rather than assigning a position — which is the
mechanism behind the whole-fragment duplication
[#464 spent a trace finding](../recent-fixes/2026-07-28-a-table-fills-one-fragmentainer-per-pass-and-is-resumed-in-the-next.md).
So the correction declines rather than retracting something it cannot put back.

The guard is exact rather than conservative: `InsertEmptyBoxes` gives row *r* a spacer with
`EndRow == r` exactly when some earlier row's cell has `startRow + rowspan - 1 == r`, and `LayoutBodyRow`
registers that same cell under `RowSpannedBoxes[startRow + rowspan - 1]`. The one hole is
`LayoutBodyRow`'s `currentColumn >= _columnWidths.Length` early `break`, which can skip the registration
while leaving the spacer in the row.

Closing it needs the retraction to reach the spanning cell's own geometry — recording what the alignment
moved, not only what the cursor gained — or the straddle question asked before the alignment runs, which
is [#390](https://github.com/jhaygood86/PeachPDF/issues/390)'s size-then-position split.
