# Page float keywords ignore an `inline` `float-reference`

css-page-floats-3 defines `float: top | bottom | ...` relative to the float's `float-reference`, whose
initial value is `inline`, so a bare `float: top` is not a page float under the specification: it is
positioned against its own line box or block. PeachPDF treats `top`/`bottom`/`top-bottom`/`snap` as
page-scoped whatever the reference says, because that is what Prince does and what documents written
for it expect. Moving the default would move every existing `float: top` document from the page edge
to an inline position.

`HtmlContainerInt.ResolvePageFloatsForThisAttempt` therefore has two kinds of area and no third:

- **`float-reference: column`** resolves against the column the float's anchor sits in
  (`ColumnRecordForPageFloat`, the same lookup a column-scoped footnote uses), and falls back to the
  page when the anchor is not inside a multi-column container, which is the specification's own
  fallback.
- **Everything else, including `inline` (the initial value), `region` and `page`,** resolves against
  the page. `region` behaves as `page` because PeachPDF has no CSS Regions; that one matches the
  specification's fallback for an unsupported reference.

Only the `inline` reading is a deviation. Keep, gate behind an opt-in, or switch the default is a
compatibility decision, not a rendering defect.

Tracked as [#1355](https://github.com/jhaygood86/PeachPDF/issues/1355).
