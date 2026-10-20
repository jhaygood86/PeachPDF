# `break-inside: avoid` on a table row is honored

Before (v0.9.23 included): `break-inside: avoid` (and `avoid-page`, or the `page-break-inside: avoid` alias) on a `<tr>` or its
cells was ignored once a cell wrapped onto several lines, so the row was cut at the page foot. Now such a row is moved whole to
the next page, under the repeated header, when it fits there; a row taller than a page is still split. Documents with rows that
avoid breaks can change page count. A row moved because it straddled a page also now starts right under the repeated header,
where it used to start one header-height lower.
