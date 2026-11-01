# `DocumentMerge` shifts the page numbers a footer prints, not the page numbers `@page` rules select by

`UseContinuousPageNumbers()` sets `HtmlDocumentOptions.PageNumberOffset` / `TotalPagesOverride`, which change what
`counter(page)` / `counter(pages)` display in margin boxes and running elements (`CssContentEngine`, `PagePainter`).
`target-counter(..., page)` is shifted too (it is the number a page would print). `@page :first`, `:left`/`:right` and page-name selection still use the page's place in its own part, and
`TotalPagesWithinSection` / `PageNumberWithinSection` count within the part.

**Why.** Page selection decides geometry and margin boxes during fragmentation, before and independently of the
displayed numbers; moving it would mean re-running fragmentation against a global page index, and for the merge the
natural reading is that each part is its own document (a cover page is its part's `:first` page). Only the printed
number needed to run on.

**Cost.** Because every part's page count is needed before any footer can be printed, a merge with continuous
numbering (or a part with several `Page` calls) lays each part out twice and runs each builder callback twice.
Counting is skipped for `Original` numbering of a single-`Page` part. A single-pass design would need the margin-box
phase of layout to be separable from body layout, which it is not today.

**Not covered.** `counter(page)` used in ordinary flowing content is not a page counter at all (see the declarative
docs); only Header/Footer content and `@page` margin boxes show the shifted numbers.
