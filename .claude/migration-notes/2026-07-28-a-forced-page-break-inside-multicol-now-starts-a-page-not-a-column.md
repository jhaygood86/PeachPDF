# A forced page break inside a multi-column container now starts a page, not a column

**Landed:** 2026-07-28 (a6171b71) — Repeat a `<thead>`/`<tfoot>` only where css-tables-3 §6.2 says it may, and fit the one that does not (#519)
**Doc section:** docs/html-css-support.md § [Multi-column Layout](../../docs/html-css-support.md#multi-column-layout)
**Verified against v0.9.6:** this note was not present in the `v0.9.6` tag's docs — confirmed genuine behavior change since 0.9.6, in scope for the next release notes.

A forced **page** break inside a multi-column container previously started the next column instead. It now starts the next page, so a document with a `break-before: page` — or a `page: <name>` transition, which forces one — inside such a container gains pages it did not have before. And a heading above content that starts the next column now starts it too, which the UA default `h1-h6 { break-after: avoid }` makes the ordinary case: content that used to fill a column to its foot may now leave the last heading's worth of space there. See [Forward compatibility](#forward-compatibility).
