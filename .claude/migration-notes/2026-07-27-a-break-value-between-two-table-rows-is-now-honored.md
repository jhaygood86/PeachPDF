# A break value declared between two table rows is now honored

**Landed:** 2026-07-27 (056ccd88) — Read a break value at the break point between two table rows
**Doc section:** docs/html-css-support.md § [Page Breaks](../../docs/html-css-support.md#page-breaks)
**Verified against v0.9.6:** this note was not present in the `v0.9.6` tag's docs — confirmed genuine behavior change since 0.9.6, in scope for the next release notes.

A forced break value declared between two **table rows** — on a `<tr>`, or on the row group one begins or ends — previously had no effect at all: the table broke only where its rows ran out of room. It is now honored, so a document carrying such a declaration gains page breaks, and possibly pages, it did not have before. See [Breaks between table rows](#breaks-between-table-rows) and [Forward compatibility](#forward-compatibility).
