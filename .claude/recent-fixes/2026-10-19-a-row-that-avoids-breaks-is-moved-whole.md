# A row that avoids breaks is moved whole

[#1835](https://github.com/jhaygood86/PeachPDF/issues/1835), found rendering business-continuity reports (long tables of
multi-line rows) against Chrome.

`CssLayoutEngineTable` read `break-inside` only on the repeated `<thead>`/`<tfoot>`. A body row was kept whole only by the
straddle correction, and `StraddleCorrectionAppliesTo` declines a stopped row — one with a cell whose lines ran out of band —
which is exactly the row `break-inside: avoid` is about. `AvoidedBreakCorrectionAppliesTo` takes that row through the same
retract-and-move arm when the row or one of its cells avoids breaks. Once per row, like the straddle correction: a row no fresh
band holds stops again where it lands, which is §4.4's relaxation, not a loop. The first body row in its band is never moved —
above it is only the band's top, a repeated header or the table's own head, and moving it would leave those alone on the page.
It also covers the row a continuation re-enters, which begins its band by construction, so the `ResumeRowIndex` check is gone.
The column decline is the straddle correction's: inside a column the row is left to the columns engine, and moved against the
page grid it was laid out in more than one column. A table inside a table cell is declined too; see
[the nested-table gap](../accepted-gaps/a-nested-table-row-moved-to-a-page-top-draws-its-last-line-twice.md).

Found by running it, not by reading: `TableRowCursor.Retract` restored bottoms, the rowspan map and foreign writes, but not
`_unfinishedCells`, so a retracted stopped row would have left the cursor reporting `Stopped` for content it no longer held.
`Retract` now clears them: a stopped row ends the pass, so every entry is the retracted row's own (recording a count to
truncate back to was always 0). The straddle correction never needed this because it never retracts a stopped row.

Also found by running it: after the retract arm re-placed a row, `rowTop` still held the abandoned placement's top, so
`SliceARowAcrossTheBandsItOverflows` measured the moved row as spanning the band it had left and displaced it by the header's
height. A moved row that only just fitted then overflowed, stopped again, and laid its last line out on both pages. The straddle
correction had the same offset on `main`. `rowTop` is now re-read after the break.

The third, from the review: a moved row that its content fits, but whose cell's bottom padding takes it past the band, is
one no band holds, so it is sliced and displaced under the next page's repeated header. Its last line may end up to
`PageBoundaryEpsilon` past the band without stopping the cell, and once displaced it lands that sliver inside the next
strip, which claimed it too (a partial line under the header, extracted twice). `FragmentEmitter.ReachesIntoItsStrip`
declines a displaced line that starts above its strip and reaches into it by no more than the tolerance line breaking itself
allows. Only the strip's top is asked: a first draft asked for overlap with the whole strip, which lost a line 0.5pt tall
or less from every strip (each band of a sliced row is displaced, the first by zero). Reached on
`main` by `break-before: page` on such a row, as well.

## Not done

No measuring pass for the row's unfragmented height: the move is attempted and the re-placement decides. A row taller than a
band therefore leaves the space it moved from blank before breaking again; Chrome does the same for a row that cannot fit.

A moved row leaves the rows above it where they are, so a page can end with only the repeated header and a short row
above the moved one more often than in Chrome (171 such pages on `main`, 177 here, 53 in Chrome over the review's corpus);
moving the rows above it too would be a keep-together rule `break-inside` does not state.

## Evidence

- `TableRowBreakInsideAvoidTests`: the three keep-together cases (row geometry via `FallsPast`: avoid on the row, on its cells,
  and after other content; `avoid` and `avoid-page`) fail on `main` and pass here; the `auto` and `avoid-column` controls pass
  on both, and fail if the checks read the column context. A moved row of 15 to 18 lines starts where `break-before: page` puts
  it and paints each line once (fails without the `rowTop` re-read); fitting rows stay on their page (fails without the
  `Stopped` check); a row holding a forced break whose later lines stop it takes the break once (fails without the forced-break
  check); the first-row check fails the 18-line case without it. Two more pin the moved row's
  bookkeeping and pass on both: a row no band holds is split with every word laid out once and no repeated header left alone
  (fails if `Retract` keeps the unfinished cells), and avoiding rows across `rowspan`s lose no word and lay out no word of their
  own cells twice (the spanning cells' own words are #1834's).
- `docs/html-css-support.md` said `break-inside: avoid` on a row "needs nothing to be honored", which is what hid #1835; it now
  says what happens to a row whose cells wrap.
- `ARowJustTallerThanAFreshBand_IsLaidOutOnce` (overshoot 0.3 and 0.45pt, moved by `avoid` and by `break-before: page`)
  fails without `ReachesIntoItsStrip`; `ANestedRowThatAvoidsBreaksAndStopsAgain_IsLaidOutOnce` fails without the nested-table
  decline; `RowsThatAvoidBreaksInAColumn_AreLeftToTheColumns` fails without the column decline; `page-break-inside: avoid`
  alone is a case of the row theory.
- `PeachBrackets.Engine.Tests`, net8.0, **Debug** build, Windows: 16,076 tests, all passing.
- Diff coverage of the changed library lines: 25/25.
- The issue's repro through `PeachPDF.Cli` now matches Chrome 149: no row cut across pages, 7 pages as in Chrome (the same file
  without the declaration still cuts R04/R08/R12/R19, as `auto` should).
