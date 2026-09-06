# Multi-column containers now reflow their own width per page

Previously, a multi-column (`columns`/`column-count`/`column-width`) container that spanned a page
boundary under per-page horizontal reflow (a `@page` rule overriding left/right margins or `size` for
some pages but not others) resolved its own width once, from the page it started on, and kept that single
column count/width for every later page it continued onto — a document mixing, say, a full-bleed first
page with narrower later pages got the first page's wider columns bleeding into the narrower pages instead
of each page laying out its own columns at its own width.

A multi-column container now resolves its width fresh for each page it continues onto, the same way an
ordinary block already did: each page gets its own column count and width, computed against that page's
own content-box width.

See [Per-page margin variation](../../docs/html-css-support.md#page-rule) in `docs/html-css-support.md`.
