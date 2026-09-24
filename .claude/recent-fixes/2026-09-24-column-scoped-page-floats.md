# `float-reference: column` page floats resolve against their column (#1272, #1269)

A `float: top`/`bottom`/`top-bottom`/`snap` page float that says `float-reference: column` inside a multi-column
container is pinned to the block-start or block-end edge of the column its source position falls in, and only that
column gives up room. Everything else - `inline` (the initial value), `page`, `region`, or a column reference outside
any multi-column container - still resolves against the page ([gap](../accepted-gaps/page-float-keywords-ignore-an-inline-float-reference.md), #1355).

## The load-bearing ideas

- **The column is recorded when the float is laid out, not inferred afterwards.** The first version found the column by
  looking up the float's `Location` in `ColumnFragmentainers`, the way a footnote call's anchor is. That is wrong for a
  float: layout moves it to its resolved edge, and a container that spans pages lays it out in a slot other than the one
  an earlier pass left it in, so a float anchored on page two was resolved against page one's column and pinned there.
  `FragmentainerContext.ColumnKey` is set by `FillColumns`, `CssLayoutEngine.FloatBoxPageArea` calls
  `HtmlContainerInt.NotePageFloatColumn`, and the last layout wins. See
  [the invariant](../invariants/page-float-a-columns-float-records-its-column-when-laid-out.md).
- **A page float inside a column is laid out unbroken.** It sits in the strip its own reservation holds back from the
  column's flow. Measured against the column's band, every word of a float's text straddles that strip, breaks into the
  next column, and takes the whole flow with it - measured as an empty first column and every paragraph in the second,
  with the float drawn over the first lines. Only a float holding text showed it, which is why the first geometry tests
  (empty boxes) all passed and rasterizing caught it. `CssBox.LayoutBlockChild` detaches the fragmentainer around it,
  as `float: left/right` already does for the same reason; the same defect was present for a *page*-referenced float
  inside a column, which this fixes too.
- **The strip feeds back into where the float's anchor falls, so it needs the footnote loop's cycle guard.** A top
  strip shortens its column, which can push the paragraph before the float into the next column, and the float follows
  it. `ColumnReservationGuard` is the #1270 mechanism extracted, one instance for footnotes and one per float edge:
  after the first repeated state each column's reservation is held at its cycle maximum. The cost is a blank strip in a
  column that lost its float. Without it the loop ended at the cap in an inconsistent state - the strip held in one
  column, the float in the other, the text under it.
- **Reservations are composed into the two calls that already exist.** `FillColumns` folds a column's note area and its
  bottom float strip into one `ReserveBandEnd` and seeds the top strip with `ReserveBandStart`, with the same
  `< target` decline the note area has (a reservation that leaves no room places nothing and the container defers page
  after page). `CssBox.ResolveBlockChildOffset` floors an in-flow child at the column's band top plus its strip.
- **`pageBudget` now subtracts the whole page-level reservation.** It subtracted only the footnote area, so a page
  `float: bottom` over a multi-column container let the columns run underneath it.

## Not done

- A container holding only floats (no in-flow content) has no column context, and a float as tall as its column is
  not reserved ([gap](../accepted-gaps/a-column-scoped-page-float-that-leaves-no-room-overflows-its-column.md)).
- `float-reference: inline` is not treated as its own reference ([#1355](https://github.com/jhaygood86/PeachPDF/issues/1355)).

## Evidence

`PageFloatColumnScopeIntegrationTests` (11 geometry tests, including the two shapes above); the pageBudget test fails
without its change. The showcase page was rasterized with PDFium and MuPDF, which agree. Full net8.0 suite green.
