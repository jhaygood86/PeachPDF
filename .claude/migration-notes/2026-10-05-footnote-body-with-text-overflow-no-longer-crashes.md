# A footnote body (and a running element) now honours `overflow` and `text-overflow`

**Before:** the content of a `float: footnote` body, and of a `position: running()` element in a margin box, was
never clipped: an `overflow: hidden` box inside it let its content run past its edge. A footnote body that declared
`overflow: hidden; text-overflow: ellipsis` made the whole render fail with
`HtmlRenderException: Exception in box paint`.

**Now:** `overflow: hidden` (and `overflow: clip`) clips the content of a box inside a footnote body or a running
element to its padding edge, as the same box does in the page, and `text-overflow: ellipsis` truncates a `nowrap`
footnote at the body's width, with an underline or other decoration ending where the ellipsis begins. The
footnote body or running element itself is clipped by nothing outside it. Text that sat outside such a clip, which
used to show, no longer does.
