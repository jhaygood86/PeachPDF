# A rowspan cell under a repeated group is built in parts

[#1834](https://github.com/jhaygood86/PeachPDF/issues/1834); follows
[2026-10-27-rowspan-cell-drawn-once](2026-10-27-rowspan-cell-drawn-once.md) (#1954).

`FragmentEmitter.ChildrenOf` yields a `CssSpacingBox`'s `ExtendedBox`, so a spanning cell is walked through its own row and
again through every placeholder in the rows it spans. #1954 builds the first visit per `FragmentKey` per walk and skips the
rest. That is right wherever every visit draws the same rectangle, and wrong under a repeated `<thead>`/`<tfoot>` with a row
of the span taller than a page: `CssLayoutEngineTable.SliceARowAcrossTheBandsItOverflows` slices that row across the bands,
shifting each strip below the repeated group and confining it to its band (`RecordFragmentDisplacement`), while the rows after
it are placed below the groups already. A single copy of the cell then has no one place. Through a sliced row it is shifted
and cut to that row's band, so keeping that one loses the rest of the cell (the fill stops short of the rows beside it; a
middle page loses it entirely). Through any other row it is drawn where its own geometry says, from the top of the page,
across the repeated group and the bands of the sliced rows.

The load-bearing idea: on a slot where a row of the span is sliced (`SlicedIn`), the cell is built in parts
(`AddRowSpanningCell`):

- its **content** (words, child boxes) once, in its own row's coordinates (that row's displacement here, or none), with its
  decoration dropped (`Draft.DropDecoration`);
- its **decoration** (background, borders) once per sliced row, confined to that row's band, and once per run of rows between
  sliced ones, confined to their extent (`StripOf`: from the cell's top for the row it starts in, to the lower of the last
  row's and the cell's bottom for the row ending the span), with its content dropped (`Draft.DropContent`) and not walked at
  all (`_decorationOnly`).

Elsewhere, #1954's rule stands unchanged, so a table with no row sliced under a repeated group builds exactly what `main`
built.

## Traps, each found by running it

- **Keeping every copy that shows something new (this PR's previous head) doubles text and translucent fills.** The cell's
  content belongs to its own row's coordinates; a copy through a later sliced row shifts it by that row's displacement and
  draws it a second time, under the repeated header (the review's `CW1 CW2 CW3` repro). And two kept copies whose visible
  spans overlap without one containing the other both paint the overlap: a darker band under an `rgba()` fill, which an
  opaque fill hides. Splitting content from decoration, and confining each decoration strip to its own row, removes both.
- **A placeholder's `ParentBox` is not its row.** The row a visit came through is looked up in the span's own rows
  (`SpannedRows.IndexOf`, built once per cell from the row group); `via.ParentBox` named a box at the table's top and
  confined the strip to nothing.
- **The cell's box can end at the foot of an earlier page.** Where its content ends before the span does,
  `CloseSpanningCell` closes the box at the page foot and states the rest as continuation shells. A decoration strip there is
  cut from the shell, unshifted (it is in the page's own coordinates), and its bottom is the row ending the span's, so where
  that row is itself sliced here its shift is added (two page-tall rows meeting on one page fell a header's height short).
- **Re-opening a page walked before the cell's height settled.** The previous head kept a draft for every placeholder that
  dropped a copy (`heldARepeat`) so that moving rows re-opened the middle page; that held a draft per placeholder per slot
  and cost up to 7.7 GB on a 4,000-row span. `CloseSpanningCell` now tells the emitter when it changes the cell's height
  (`InvalidateEmittedFragmentsFor`), which re-opens exactly the pages emitted with the cell at its opening height.
- **Cost.** Every decision is made before building: a skipped visit builds nothing (as #1954), a decoration strip does not
  walk the cell's content, the spanned rows and their indices are found once per cell, sliced rows are found among the boxes
  displaced anywhere (few) rather than among the rows spanned (thousands), and consecutive unsliced rows share one strip. A
  strip per row, before the last change, was 55% slower than `main` on a 2,000-row span with page-tall rows.

## Not done

- **#2313**: since #1950, a cell beside a spanning cell is cut at the page foot instead of filling its row, so the
  continuation page shows the spanning cell with nothing beside it. A layout regression, not part of this change; the
  `("none", "15,15", "0", 300)` case is left out of `TableRowspanCellPaintedOnceTests` until it is fixed.
- **#2314**: under a repeated group, a cell whose content is taller than a page and aligned `middle` or `bottom` loses the
  text after its tall block. 144 of the sweep's 576 layouts, identical on `main`.

## Evidence

- `TableRowspanCellPaintedOnceTests`: 18 of the 30 continuation cases fail on `main` with #1954; all pass. The new
  `ARowspanCellUnderARepeatedGroup_IsDrawnOnceAndNeverOverTheGroup` (the review's repros with a translucent fill, plus a
  tall-first-row case) checks every page for a doubled fill, a fill over the repeated group's label, and every word once.
- A throwaway sweep of 576 layouts (group none / `thead` / `tfoot` / both, separate and collapsed, `vertical-align` top /
  middle / bottom, short and taller-than-a-page content, spans 2 to 4 with the page-tall row in each position or none),
  translucent fill: this change has 0 doubled words, 0 doubled fills, 0 fills over a repeated label; `main` has 108 layouts
  with the cell over the label. Both lose `CW3` in the same 144 (#2314).
- Release, net10.0, warm, best of three (one cell spanning every row; page-tall row every 10th): 2,000 rows with a `thead`
  1.16 s against `main`'s 1.04 s; 1,000 rows 0.26 s against 0.20 s; without a `thead` 0.63 s against 0.60 s; 2,000 short
  rows with a `thead` 0.22 s against 0.22 s. Allocation within 7% of `main` in each.
