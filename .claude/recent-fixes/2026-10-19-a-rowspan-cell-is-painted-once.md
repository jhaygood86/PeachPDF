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
- **Leaving a repeat out may not change which boxes hold a draft.** Which boxes hold drafts decides which frozen slots layout
  re-opens when it moves them (`HoldsFragmentsFor` → `InvalidateFrom`), and a row and its placeholder held a draft on a page
  only through the cell's copy there. Without it, moving those rows never re-opened a page the emitter had walked while the
  cell's height was still unsettled (its bounds still its first 18pt), so the `Finish` replay never redrew it: the middle page
  of a span of 5 with a page-and-two-thirds-tall own row, `vertical-align: top`, under a repeated `<thead>`/`<tfoot>`, lost
  the whole cell. The box that left a repeat out still gets its draft (`heldARepeat` in `BuildDraftCore`), as it did when the
  copy was kept. Found by the reviewer; `ARowspanCellOverAPageTallOwnRow_IsDrawnOnTheMiddlePage` pins it.
- A continuation shell draws at `ShellRect`, not at the cell's bounds, so that is the rectangle compared. An earlier version
  of this note called it a measured trap; it is not: with both copies' bounds outside the slot their spans share one bottom,
  and every layout tried gives the same pages without it.
- The `EmitSlot` reset is no longer observable in output: spans are clipped to the slot's region, so an entry from an earlier
  slot never contains a later copy. It stays to bound the set to one slot. The reset before `VerifyAgainstTheFullWalk`'s
  reference walk is observable, and pinned.

## Not done, and found to be older than this change

With a page-tall row in the span under a repeated group, the copy reached through a row that is not on the page draws at its
undisplaced place: it can paint the cell over the repeated `<thead>`, run past its last row (a `300pt` row followed by a
`150pt` one), and, with two page-tall rows, leave the last few points of the cell undrawn on the final page. The second-round
head and `main` draw the same copies there (`main` draws more on top), so none of this is a regression; the tests use row
heights that avoid it. The same class doubles words: where the spanning cell's own content runs down several bands under a
repeated group, the copy through an undisplaced row draws the words of a displaced band again, unshifted (a `rowspan="2"`
cell 300pt tall under a `<thead>`: `w1_7`…`w1_10` painted on both page 1 and page 2). Measured identically on the
third-round head; clipping alone would not remove it from a PDF reader's text, since clipped text is still extracted.

**Row backgrounds now show the table background bug that `main` hid.** A row's background is painted over the row's whole
rectangle, in tree order, including the part of a cell spanning in from an earlier row; CSS 2.1 §17.5.1 has a row's background
cover only "the full area of all cells that originate in the row", under every cell. On `main` the duplicate copy under each
later row repainted the cell over that row's background. With one copy kept, a later spanned row's `<tr>` background paints
over the cell: measured as row 2's fill drawn after the cell's fill and text in a `rowspan="3"` table with a background on
row 2. Keeping the *last* copy instead is not the fix: `PdfTagRecorder` parents an element under whichever element opens it
first, so the cell's `TD` would move under the last row's `TR`. The fix belongs to table background painting
(css-tables-3 §5.3.2: each cell draws its column-group, column, row-group and row backgrounds): [#1894](https://github.com/jhaygood86/PeachPDF/issues/1894), fixed by [#1895](https://github.com/jhaygood86/PeachPDF/pull/1895), which should land first.

## Evidence

- `TableRowspanCellPaintedOnceTests` (net8.0, Debug): the one-page censuses fail on `main`; the continuation theory (repeated
  `thead`, two-row `thead`, `tfoot`, none; tall row first or second; two and three rows; two page-tall rows; a page-tall
  spanning cell) checks on every continuation page that the cell's fill reaches as far as the rows beside it, that its bottom
  border strip is drawn on the last page, that no visible strip is drawn twice, and that every word is painted once. Its
  tall-second-row cases fail on the second-round head (66538f1e), the middle-page test fails on the third-round head
  (2ed05014), and 25 of the class's 31 tests fail on `main`.
- Mutants that fail tests: removing `heldARepeat`, the fragmentainer-bottom or band-top clip, the top half of "already shows",
  or the reset before the reference walk. Mutants that survive, each alone: the fragment key or the rectangle in the
  identity, `ShellRect`, the fragmentainer-top or band-bottom clip, the bottom half of "already shows". No layout tried puts
  two copies at one rectangle whose spans differ by one of these; `ShowsSomethingNew`'s remarks say why they stay.
- `ARowspanCellOverAPageTallOwnRow_IsDrawnOnTheMiddlePage` (the reviewer's repro, `<thead>` and `<tfoot>`): the middle page
  draws no cell on the third-round head; passes here and on `main`.
- `PeachBrackets.Engine.Tests`, net8.0: 16,086 passing in Debug, 16,072 in Release (Release leaves out DEBUG-only tests).
- Diff coverage of the changed library lines: 31/32 (coverlet Cobertura, per changed line). The line left is `heldARepeat` in
  the ordinary-children loop: a cell reached through its own row comes first, so it is never a repeat there.
