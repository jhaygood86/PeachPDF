# A row that avoids breaks is moved whole

[#1835](https://github.com/jhaygood86/PeachPDF/issues/1835), found rendering business-continuity reports (long tables of
multi-line rows) against Chrome.

`CssLayoutEngineTable` read `break-inside` only on the repeated `<thead>`/`<tfoot>`. A body row was kept whole only by the
straddle correction, and `StraddleCorrectionAppliesTo` declines a stopped row — one with a cell whose lines ran out of band —
which is exactly the row `break-inside: avoid` is about. `AvoidedBreakCorrectionAppliesTo` takes that row through the same
retract-and-move arm when the row or one of its cells avoids breaks. Once per row, like the straddle correction: a row no fresh
band holds stops again where it lands, which is §4.4's relaxation, not a loop. The first body row in its band is never moved —
above it is only the band's top, a repeated header or the table's own head, and moving it would leave those alone on the page.
That guard is defensive: no fixture tried here (a 400-word row, a 400pt block in a cell) made the row loop's estimate break before
an avoiding row that then stopped, so no test fails without it.

Found by running it, not by reading: `TableRowCursor.Retract` restored bottoms, the rowspan map and foreign writes, but not
`_unfinishedCells`, so a retracted stopped row would have left the cursor reporting `Stopped` for content it no longer held.
`RowPlacement` now carries the unfinished count and `Retract` truncates back to it. The straddle correction never needed this
because it never retracts a stopped row.

## Not done

No measuring pass for the row's unfragmented height: the move is attempted and the re-placement decides. A row taller than a
band therefore leaves the space it moved from blank before breaking again; Chrome does the same for a row that cannot fit.

## Evidence

- `TableRowBreakInsideAvoidTests`: the three keep-together cases (row geometry via `FallsPast`: avoid on the row, on its cells,
  and after other content) fail on `main` and pass here; the `auto` control passes on both. Two more pin the moved row's
  bookkeeping and pass on both: a row no band holds is split with every word laid out once and no repeated header left alone
  (fails if `Retract` keeps the unfinished cells), and avoiding rows across `rowspan`s lose no word and lay out no word of their
  own cells twice (the spanning cells' own words are #1834's).
- `docs/html-css-support.md` said `break-inside: avoid` on a row "needs nothing to be honored", which is what hid #1835; it now
  says what happens to a row whose cells wrap.
- Full suite on net8.0: 17,452 tests, the one failure (`AnchorLinkDestinationTests…NearThePageTop`) also fails on `main`.
- Diff coverage of the changed library lines: 24/24.
- The issue's repro through `PeachPDF.Cli` now matches Chrome 149: no row cut across pages, 7 pages as in Chrome (the same file
  without the declaration still cuts R04/R08/R12/R19, as `auto` should).
