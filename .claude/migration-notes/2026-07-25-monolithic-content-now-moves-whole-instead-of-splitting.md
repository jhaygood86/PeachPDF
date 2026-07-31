# Monolithic content (non-`visible` overflow) now moves whole instead of being cut by a page break

**Landed:** 2026-07-25 (d81f5363) — Name what monolithic means, and honour the spec's own set
**Doc section:** docs/html-css-support.md § [Page Breaks](../../docs/html-css-support.md#page-breaks)
**Verified against v0.9.6:** this note was not present in the `v0.9.6` tag's docs — confirmed genuine behavior change since 0.9.6, in scope for the next release notes.

A box with `overflow` other than `visible` — very often a card, panel or figure with `overflow: hidden` — used to be cut in half by a page boundary. It now moves to the next page whole, which leaves a gap where it used to sit and shifts the content after it. See [Forward compatibility](#forward-compatibility).
