# A header-opened rowspan cell crossing into the body must stay unclosed until the body closes it

_Table `rowspan`/pagination. Tracker: [#788](https://github.com/jhaygood86/PeachPDF/issues/788)._

`CssLayoutEngineTable.SeedCrossBoundaryRowSpans` registers a header cell whose `rowspan` reaches into
`<tbody>` into the body `TableRowCursor`'s own `RowSpannedBoxes`, at the row its span actually ends on -
making the body row loop's existing `CloseSpanningCell`/straddle-correction/pagination machinery treat it
as an indistinguishable, ordinary already-open rowspan cell, with no changes to that machinery itself.
This only works because `RegisterRowSpanCellsEndingRow`'s header call site is given
`_headerRowSpansCrossingIntoBody` and explicitly *skips* registering such a cell into the header's own
group-local closing bookkeeping (`headerSpanningCellsEndingOnRow`) - so the cell is left in its natural,
un-stretched, un-aligned state (whatever `LayoutBodyRow`'s own layout of the header's opening row gave it)
right up until the body loop's `CloseSpanningCell` closes it for real, later.

**If a future change to `DetachAndMeasureRepeatedRowGroups`'s header loop ever stops excluding a crossing
cell there** - e.g. simplifying by dropping the now-seemingly-redundant `crossingCells` parameter, since
every `rowSpan > 1` cell "should" be registered - the header loop would pre-stretch/close the cell against
the header's own bottom. `CloseSpanningCell`'s later close from the body would then compose its own
alignment offset on top of that already-applied one instead of replacing it (per `TableRowCursor
.RecordForeignWrite`'s own doc, the offset "is neither idempotent nor derivable after the fact"),
corrupting the cell's content position - a defect a `CssBox`-property test asserting only `ActualBottom`
could miss, since the final `ActualBottom` may still look plausible while the *content inside* the cell
has been shifted twice.

A second, related trap: `CreateHeaderProxy`/its `PerformLayout` (in `LayoutBodyRows`) runs *before* the
body row loop, so a repeating header's own painted snapshot (`CssProxyBox.SourceGeometry`, what
`FragmentEmitter` actually builds the header's fragments from) is captured while a crossing cell is still
in that natural, un-closed state - correct for the live box, wrong for the snapshot, a layout/paint
divergence a live-box-only test cannot catch (see this repo's own testing-conventions note on exactly this
trap). `CloseSpanningCell` resyncs every header proxy that already exists at the moment a crossing cell
closes (`CssLayoutEngineTable.ResyncHeaderProxiesFor`, `BoxGeometrySnapshot.Resync`) - a future change that
adds a *new* way to close such a cell (bypassing `CloseSpanningCell` itself) must call the same resync, or
reintroduce the identical divergence.
