# A line mixing different content heights no longer splits across a page break

**Landed:** 2026-07-25 (c8d7632b) — Document fragmentation during layout, and drop dead context surface
**Doc section:** docs/html-css-support.md § [Page Breaks](../../docs/html-css-support.md#page-breaks)
**Verified against v0.9.6:** this note was not present in the `v0.9.6` tag's docs — confirmed genuine behavior change since 0.9.6, in scope for the next release notes.

A line mixing content of different heights — an inline image, or a larger font, beside ordinary text — used to be able to split at a page boundary, with the taller content moving to the next page while the shorter words stayed behind on the page the rest of their line had left. Such a line now moves as a unit. See [Forward compatibility](#forward-compatibility).
