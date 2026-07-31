# Continuation content now starts exactly at the page's content edge

**Landed:** 2026-07-25 (c8d7632b) — Document fragmentation during layout, and drop dead context surface
**Doc section:** docs/html-css-support.md § [Page Breaks](../../docs/html-css-support.md#page-breaks)
**Verified against v0.9.6:** this note was not present in the `v0.9.6` tag's docs — confirmed genuine behavior change since 0.9.6, in scope for the next release notes.

Continuation content used to begin one layout unit (1/96in) below the content edge, so everything after a page break sat very slightly lower than the page's own top margin implied. It now starts exactly at the edge, which shifts continuation content up by that amount and can let one more line fit on a page. See [Forward compatibility](#forward-compatibility).
