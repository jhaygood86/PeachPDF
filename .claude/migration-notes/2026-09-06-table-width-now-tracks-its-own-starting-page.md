# A table's own width now tracks whichever page it starts on

Previously, a table's own width (and therefore its column widths) resolved against its containing
block's single, cached `Size.Width` — a value fixed at whatever page the containing block (typically
`<body>`, which always itself starts on the document's first page) was measured against. A table forced
onto a later page with different `@page` margins (via a forced page break, or simply following enough
preceding content) kept that earlier, unrelated page's width instead of its own.

A table's width now resolves against the page it itself starts on. Unlike multi-column layout, this does
not vary further as the table continues across later pages: a table's columns are one shared grid spanning
every row, so a table that itself starts on one page and continues onto others keeps one consistent width
and column layout for its whole lifetime, correctly reflecting wherever it began.

See [Per-page margin variation](../../docs/html-css-support.md#page-rule) in `docs/html-css-support.md`.
