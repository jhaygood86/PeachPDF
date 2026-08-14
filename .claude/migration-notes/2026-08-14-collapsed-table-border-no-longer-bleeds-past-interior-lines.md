# A `border-collapse: collapse` table's cell background no longer bleeds past an interior border

Before this fix, an interior horizontal or vertical grid line in a `border-collapse: collapse`
table painted its resolved border centered on the *edge* of the row/column overlap band rather
than the band's true center. In practice this meant the border segment only covered roughly the
half of the overlap nearer one side, leaving the other half showing through as a thin sliver of
the neighboring cell's background color past the border line - most visibly a `background-color`
appearing to bleed about half a border-width above/below (or left/right of) a `border-bottom`/
`border-right`/etc. line shared between two collapsed cells.

Document authors relying on a collapsed table's border visually meeting an adjacent cell's
background with no gap or overlap (a common pattern for colored status/alert rows) will now see
the border fully cover the shared line, with no bleed. This also applies to a repeating
`<thead>`/`<tfoot>`'s own boundary line to the table body, and to a vertical column divider that
spans a repeating header/footer's full row range.

No markup or CSS change is needed - this is a rendering correctness fix with no new API surface.
