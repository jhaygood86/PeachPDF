# A rowspan cell keeps each copy that shows a different part of it

[#1834](https://github.com/jhaygood86/PeachPDF/issues/1834); follows
[2026-10-27-rowspan-cell-drawn-once](2026-10-27-rowspan-cell-drawn-once.md) (#1954).

`FragmentEmitter.ChildrenOf` yields a `CssSpacingBox`'s `ExtendedBox`, so a spanning cell is walked through its own row and
again through every placeholder in the rows it spans. #1954 kept the first copy per `FragmentKey` per walk and returned null
for the rest. That is right on a page where every copy draws the same rectangle, and wrong under a repeated
`<thead>`/`<tfoot>`: `CssLayoutEngineTable` displaces a row taller than a page and confines each row's run to its own band, so
each row's copy of the cell is clipped to a different band and draws a different part of it. Keeping the first lost the rest.

The load-bearing idea is to deduplicate per *copy*, by what it shows. Every copy is built; after `BuildDraft`,
`ShowsSomethingNew` drops it only when an earlier copy in the same walk has the same `FragmentKey`, lands at the same rectangle
(its `ShellRect`, or its own bounds, moved by its `Shift`) and already shows everything this one can show: the block-axis span
of that rectangle inside the fragmentainer (`Region`) and inside the band a displaced run confines it to (`ConfinedTo`).
`_rowSpanningCellsThisWalk` now holds those spans, gated on #1954's `IsRowSpanningCell`.

## Traps, each found by running it

- **Per cell is wrong under a repeated `<thead>`/`<tfoot>`** (#1954's rule, and this PR's second round). With the tall row
  second, the copy kept was clipped to the first row's band: no fill, side or bottom border below it on the continuation
  page. 18 of `TableRowspanCellPaintedOnceTests`' 30 cases fail on `main` with #1954, this or the trap below.
- **Per (cell, rectangle, band), exactly, is wrong too.** On a slot where one copy is confined to a band and another at the
  same rectangle is not, both survive and the text doubles. Containment of the visible span is what tells a repeat apart.
- **Leaving a repeat out may not change which boxes hold a draft.** Which boxes hold drafts decides which frozen slots layout
  re-opens when it moves them (`HoldsFragmentsFor` → `InvalidateFrom`), and a row and its placeholder held a draft on a page
  only through the cell's copy there. Returning null for the repeat (as #1954 did) or dropping it outright lost that, so the
  `Finish` replay never redrew a page walked while the cell's height was unsettled: the middle page of a span of 5 with a
  page-and-two-thirds-tall own row, `vertical-align: top`, under a repeated `<thead>`/`<tfoot>`, lost the whole cell (fails on
  `main` with #1954). The box that left a repeat out still gets its draft (`heldARepeat` in `BuildDraftCore`).
- A continuation shell draws at `ShellRect`, so that is the rectangle compared; no tested layout tells it from the cell's own
  bounds.

## Not done, and older than this change

- With a page-tall row in the span under a repeated group, the copy reached through a row that is not on the page draws at its
  undisplaced place: it can paint the cell over the repeated `<thead>`, run past its last row, or leave the last few points of
  the cell undrawn on the final page. The same class doubles words where the cell's own content runs down several bands. The
  repeated copies used to hide some of these copies under a later fill; a stray continuation-page copy can now be visible
  where `main` before #1954 hid it.
- Since #1950 (2026-10-27), a cell beside a spanning cell is cut at the page foot (`ActualBottom` at the foot) instead of
  filling its row, which runs on to the spanning cell's bottom, so the continuation page shows the spanning cell with nothing
  beside it. Measured with the probe `rowspan=2` cell 300pt tall beside two 15pt rows: the second row ends at 320, its cell at
  230 (320 before #1950). A layout regression, not part of this change; the matching case
  (`"none", "15,15", "0", 300`) is left out of the theory until it is fixed.

## Evidence

- `TableRowspanCellPaintedOnceTests` (30 cases, net8.0, Debug): 18 fail on `main` with #1954 (the continuation theory under
  a repeated group, the middle-page test, the two page-tall rows meeting on one page); all pass here, as do #1954's
  `RowspanCellPaintsOnceTests`.
- Mutants measured on the earlier head of this change (`a7084c44`): removing `heldARepeat`, the fragmentainer-bottom or
  band-top clip, the top half of "already shows", or the reset before the reference walk each fail tests; the fragment key or
  rectangle in the identity, `ShellRect`, the fragmentainer-top or band-bottom clip and the bottom half of "already shows"
  survive alone (see `ShowsSomethingNew`'s remarks).
