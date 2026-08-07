# `visibility: collapse` table layout: a cell straddling a collapsed and a visible column

Tracking issue: [#667](https://github.com/jhaygood86/PeachPDF/issues/667) (follow-up to
[#639](https://github.com/jhaygood86/PeachPDF/issues/639), which implemented CSS 2.1
[§17.6.1](https://www.w3.org/TR/CSS21/tables.html#dynamic-effects) table `visibility: collapse`
layout, and [#665](https://github.com/jhaygood86/PeachPDF/issues/665), which closed the
rowspan-crossing-a-collapsed-row and collapsed-column-content-width-leakage gaps `#639` left open).

Both #639's and #665's fixes gate on `CssLayoutEngineTable.CellOccupiesOnlyCollapsedColumns`, which
is a binary check: every column a cell's `colspan` reaches is collapsed, or none of the special-casing
applies. A `colspan` cell that straddles a collapsed column together with a visible one gets neither
treatment:

1. **Border-spacing.** `LayoutBodyRow` only skips the border-spacing slot after a cell when it
   occupies solely collapsed column(s). A straddling cell still gets a full spacing slot after it,
   even though one of the columns it spans contributes no width of its own.

2. **Content-based column width.** `GetColumnsMinMaxWidthByContent`/`GetColumnMinWidths` only skip a
   cell's content contribution when it occupies solely collapsed column(s). A straddling cell's
   content is divided evenly across every column it spans (by `colspan`) and contributed to each -
   including the collapsed one (harmless, since it is zeroed by `CollapseColumnWidths` regardless)
   and the visible one it also spans, where the even division does not account for one of the spanned
   columns being collapsed and so contributing nothing back, understating the visible column's fair
   share of the straddling cell's own content-based width.

**Deliberately out of scope.** This is a narrower edge case than either #639's or #665's own scope -
a `colspan` cell that specifically straddles a collapsed and a visible column, rather than a rowspan
crossing a collapsed row or a cell confined entirely to one collapsed column. Fixing it means teaching
the colspan-width-apportionment logic (`GetColumnsMinMaxWidthByContent`, `GetColumnMinWidths`, and the
border-spacing check in `LayoutBodyRow`) to treat a straddling cell's collapsed and visible columns
asymmetrically, rather than the binary all-or-nothing check `CellOccupiesOnlyCollapsedColumns`
provides today.
