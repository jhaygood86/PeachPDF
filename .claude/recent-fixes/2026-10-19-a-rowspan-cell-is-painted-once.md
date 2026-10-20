# A rowspan cell is painted once

[#1834](https://github.com/jhaygood86/PeachPDF/issues/1834), found rendering business-continuity reports with merged cells
against Chrome.

`FragmentEmitter.ChildrenOf` yields a `CssSpacingBox`'s `ExtendedBox`, so a spanning cell is walked through its own row and
again through every placeholder in the rows it covers. Its comment assumed each copy lands "in its own place", but every copy
reads the same live geometry, so within a slot the copies were the same cell at the same rectangle, and the paint log drew each
word 2N−1 times (3 for `rowspan="2"`, 5 for `rowspan="3"`): bolder on the page and extracted 2N−1 times by a reader.

The load-bearing idea is to deduplicate per *copy*, not per cell. Every copy is still built; after `BuildDraft`,
`ShowsSomethingNew` drops it when an earlier copy in the same walk has the same `FragmentKey`, lands at the same
rectangle (its `ShellRect`, or its own bounds, moved by its `Shift`) and already shows everything this one can show: the
block-axis span of that rectangle inside the fragmentainer (`Region`) and inside the band a displaced run confines it to
(`ConfinedTo`). `_spanningCellCopiesThisWalk` holds those spans and is cleared wherever `_liveFloatWordsThisWalk` is.

## Traps, each found by running it

- **Per cell is wrong under a repeated `<thead>`/`<tfoot>`.** `CssLayoutEngineTable` displaces a row taller than a page and
  confines it to one band per page, so each row's copy of the cell is clipped to that row's band and draws a *different* part
  of it. Keeping the first copy per cell (the second review round's version) lost the part below the cell's own row when the
  tall row was the second one: no fill, side or bottom border there.
- **Per (cell, rectangle, band), exactly, is wrong too.** That is the reviewer's prototype; on a slot where one copy is
  confined to a band and another at the same rectangle is not, both survive and the text doubles (the first page of a
  tall-first table, and the page a tall second row starts on). Containment of the visible span is what tells a repeat apart.
- **A continuation shell draws at `ShellRect`, not at the cell's bounds.** The cell's own `Location`/`Size` describe its
  first fragment; a later fragment is the rectangle layout stated. Comparing bounds made two different shell copies look
  identical (both empty in the slot) and dropped the one that showed the cell on a page where two tall rows meet.
- The `EmitSlot` reset is no longer observable in output: spans are clipped to the slot's region, so an entry from an earlier
  slot never contains a later copy. It stays to bound the set to one slot. The reset before `VerifyAgainstTheFullWalk`'s
  reference walk is observable, and pinned.

## Not done, and found to be older than this change

With a page-tall row in the span under a repeated group, the copy reached through a row that is not on the page draws at its
undisplaced place: it can paint the cell over the repeated `<thead>`, run past its last row (a `300pt` row followed by a
`150pt` one), and, with two page-tall rows, leave the last few points of the cell undrawn on the final page. The second-round
head and `main` draw the same copies there (`main` draws more on top), so none of this is a regression; the tests use row
heights that avoid it.

**Row backgrounds now show the table background bug that `main` hid.** A row's background is painted over the row's whole
rectangle, in tree order, including the part of a cell spanning in from an earlier row; CSS 2.1 §17.5.1 has a row's background
cover only "the full area of all cells that originate in the row", under every cell. On `main` the duplicate copy under each
later row repainted the cell over that row's background. With one copy kept, a later spanned row's `<tr>` background paints
over the cell: measured as row 2's fill drawn after the cell's fill and text in a `rowspan="3"` table with a background on
row 2. Keeping the *last* copy instead is not the fix: `PdfTagRecorder` parents an element under whichever element opens it
first, so the cell's `TD` would move under the last row's `TR`. The fix belongs to table background painting
(css-tables-3 §5.3.2: each cell draws its column-group, column, row-group and row backgrounds), tracked as its own bug.

## Evidence

- `TableRowspanCellPaintedOnceTests` (net8.0, Debug): the one-page censuses fail on `main`; the continuation theory (repeated
  `thead`, two-row `thead`, `tfoot`, none; tall row first or second; two and three rows; two page-tall rows; a page-tall
  spanning cell) checks on every continuation page that the cell's fill reaches as far as the rows beside it, that its bottom
  border strip is drawn on the last page, that no visible strip is drawn twice, and that every word is painted once. 4 of its
  cases fail on the second-round head (the tall-second-row ones) and 22 of the class's tests fail on `main`.
- Mutants: dropping the region clip, dropping the band clip, or the reset before the reference walk each fail tests.
- `PeachBrackets.Engine.Tests`, net8.0, Debug: 16,081 tests, all passing.
- Diff coverage of the changed library lines: 26/26 (coverlet Cobertura, per changed line).
