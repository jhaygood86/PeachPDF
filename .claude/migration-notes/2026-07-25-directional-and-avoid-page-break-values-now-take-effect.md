# Directional (`left`/`right`/`recto`/`verso`) and `avoid-page` break values now take effect

**Landed:** 2026-07-25 (99b6a5e4) — Document directional breaks, and show them as a book
**Doc section:** docs/html-css-support.md § [Page Breaks](../../docs/html-css-support.md#page-breaks)
**Verified against v0.9.6:** this note was not present in the `v0.9.6` tag's docs — confirmed genuine behavior change since 0.9.6, in scope for the next release notes.

`left`, `right`, `recto` and `verso` (in both the `break-*` and `page-break-*` spellings) previously produced no page break at all. They now force one, and insert a blank page where the requested side calls for it, so a document using them gains page breaks — and possibly pages — it did not have before. Likewise `break-inside: avoid-page` and `break-before`/`break-after: avoid-page` previously had no effect and now behave as their bare `avoid` counterparts do. See [Forward compatibility](#forward-compatibility).
