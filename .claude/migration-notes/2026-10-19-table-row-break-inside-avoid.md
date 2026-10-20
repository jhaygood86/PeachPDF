# `break-inside: avoid` on a table row is honored

Before (v0.9.23 included): `break-inside: avoid` (and `avoid-page`, or the `page-break-inside: avoid` alias) on a `<tr>` or its
cells was ignored once a cell wrapped onto several lines, so the row was cut at the page foot. Now such a row is moved whole to
the next page, under the repeated header, when it fits there; a row taller than a page, or a row of a table nested in another
table's cell, is still split. Documents with rows that avoid breaks can change page count.

Two changes reach documents with no `avoid` at all. A row moved to the next page because it straddled the page foot now starts
right under the repeated header, where it used to start one header-height lower; that moves such a row's top closer to where
browsers put it, and a document can gain or lose a page by it. And a row that overflows the page by little more than its cell's
bottom padding, under a repeated header, no longer draws its last line a second time, mostly clipped, at the top of the next page
(a PDF reader used to extract that line twice).
