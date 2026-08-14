# Collapsed table borders now resolve per CSS 2.1 §17.6.2, instead of a flat 1pt row/column overlap

Previously: a `border-collapse: collapse` table made adjacent rows/columns overlap by a flat, hardcoded
1 point regardless of the actual border widths declared anywhere in the table, and every participating box
(the `<table>`, each `<tr>`, each `<td>`) painted its own border independently, relying on that overlap for
them to visually coincide rather than resolving one winning border per shared edge. Two visible defects
followed directly from this:

- **A row's border could be painted over and erased by the next row's own background**
  ([#735](https://github.com/jhaygood86/PeachPDF/issues/735)): because boxes paint in DOM order, a later
  row's opaque cell background — nudged by the flat overlap to reach into the row above it — could land on
  top of, and erase, the shared border. This is fixed: resolved borders now paint once, after every
  table-internal background, so they're never erased regardless of paint order elsewhere.
- **Table dimensions were wrong whenever cells disagreed on a shared border**, or when no border was
  declared on a shared edge at all: the flat 1pt overlap applied unconditionally, shrinking a
  border-less collapsed table by 1pt at every internal row/column boundary even though nothing justified
  the overlap, and never widening it to accommodate a wider declared border either.

Now: a collapsed table's borders resolve per CSS 2.1
[§17.6.2](https://www.w3.org/TR/CSS21/tables.html#border-conflict-resolution) — for each grid line, the
widest declared border wins; ties break by style priority (`hidden` > `double` > `solid` > `dashed` >
`dotted` > `ridge` > `outset` > `groove` > `inset` > `none`, `hidden` always suppressing the edge
outright); further ties break by origin specificity (cell > row > row-group > column > column-group >
table) and finally by position. Adjacent rows/columns overlap by exactly that resolved width instead of a
flat constant — so:

- **An existing collapsed table's rendered size changes** wherever a shared edge's resolved width differs
  from 1pt: a table with e.g. 3pt borders is now correctly ~2pt narrower per column boundary and ~2pt
  shorter per row boundary than before (previously undersized by only 1pt regardless of declared width);
  a table with no border at all on some shared edge is now exactly flush there instead of overlapping by a
  spurious 1pt.
- **Only one border is drawn per shared edge**, chosen by the rules above, instead of every participating
  box drawing its own — so mismatched or doubled borders on a shared edge now resolve the way a browser
  resolves them, and `border-style: hidden` now actually suppresses that edge rather than being drawn like
  any other style.
- **`<col>`/`<colgroup>` now participate in both border resolution and painting**, where before they had no
  paint code path at all: a `<col>`'s own `border`/`background-color` is now a real candidate in the
  origin-priority tiebreak above, and its background now layers correctly between the table's own
  background and its column's cells' backgrounds (CSS 2.1 §17.5.1's table → column-group → column →
  row-group → row → cell order), including across a page break.
- **A repeated `<thead>`/`<tfoot>` now resolves and paints its own collapsed borders correctly on every
  page it repeats on**, not only the first: the group's own internal grid lines (between its own rows, for
  a multi-row header/footer) are redrawn at each page's own position, and the border where the group meets
  the table body is re-resolved fresh against whichever row actually starts/ends that specific page —
  border-collapse is about visual adjacency, and a repeat's neighbor differs per page even though its
  DOM-order neighbor does not. Previously this boundary was resolved once, against the group's single
  DOM-order neighbor, and silently reused (or altogether misplaced) on every later page.
