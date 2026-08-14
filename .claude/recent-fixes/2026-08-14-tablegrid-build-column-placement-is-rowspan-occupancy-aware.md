# TableGrid.Build works out a cell's real column itself instead of trusting a Boxes-list sum, closing issue #736

[Issue #736](https://github.com/jhaygood86/PeachPDF/issues/736): `TableGrid.Build` used to take an injected
`getRealColumnIndex(row, cell)` callback (`CssLayoutEngineTable.GetCellRealColumnIndex`) that summed a
row's own preceding cells' colspans to find where a cell starts. Correct for an ordinary body row, since
`CssLayoutEngineTable.InsertEmptyBoxes` pads a rowspan's gap with a `CssSpacingBox` placeholder before this
runs - never correct for a detached `<thead>`/`<tfoot>` group's own rows, which `InsertEmptyBoxes` never
touches (only `_bodyRows`). A rowspan cell starting in one header/footer row and reaching into a later row
of the same group left every later cell in that row placed one or more columns short, and `TableGrid.Build`'s
`slots[...] ??= cell` first-writer-wins fill then silently dropped the later cell from the grid for the
column(s) it should have occupied - so any collapsed-border resolution reading that slot lost the cell's
own declared border as a candidate.

## The fix

`TableGrid.Build` no longer takes a column-index callback at all. It works out each cell's real column
itself, one row at a time, tracking which columns an earlier row's rowspan has already claimed and through
which row (`columnOccupiedThroughRow`, a list indexed by column holding the last row each is occupied
through) and skipping a cell's placement forward past any conflict - the standard HTML table
"downward-growing cell" placement algorithm. This is self-sufficient: it doesn't need `CssSpacingBox`
placeholders to exist at all, which is exactly why it's correct for header/footer rows where they don't.
Each cell's `(column, colSpan, lastRow)` is cached from this first pass and reused by the second (which
builds `slots`/`spans`) rather than recomputed, since `getLastRow` forwards to
`CssLayoutEngineTable.GetEffectiveEndRowIndex` - an O(rows) scan for any `rowSpan > 1` cell - and paying
that twice per such cell was avoidable.

## What was deliberately not done

`CssLayoutEngineTable.GetCellRealColumnIndex` itself is unchanged and still has the identical bug - it's
called directly by `LayoutBodyRow` (reused for header/footer row layout) to compute a cell's rendered
width and, indirectly, its X position. So after this fix, `D`'s collapsed border resolves at the right
column in the shape below, but `D` still *renders* at the wrong column/width - a distinct defect through a
different code path, confirmed by measuring the laid-out box tree directly rather than assumed, and now
tracked separately as
[#740](https://github.com/jhaygood86/PeachPDF/issues/740) (see the accepted-gap file for why it needs a
different fix than this one - `LayoutBodyRow` needs an actual column index up front, not grid topology it
can query after the fact).

## Evidence

New regression test (`CollapsedBorderModelIntegrationTests.Issue736Repro_...`) reproduces the issue's exact
shape and asserts both grid occupancy (`TableGrid.CellAt`) and the resolved border width. Full suite green
(8826 tests, net8.0), zero warnings on `dotnet build -t:Rebuild`, 100% diff coverage on the changed lines
(`diff-cover` against `main`).
