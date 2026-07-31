# A relocated box is now laid out again at its new page

**Landed:** 2026-07-25 (ce8924be) — Document what a relocated box now does, and show it
**Doc section:** docs/html-css-support.md § [Page Breaks](../../docs/html-css-support.md#page-breaks)
**Verified against v0.9.6:** this note was not present in the `v0.9.6` tag's docs — confirmed genuine behavior change since 0.9.6, in scope for the next release notes.

A relocated box used to be shifted to its new page with the layout it had at the old one, so a box whose text had already crossed the boundary kept that page gap inside it as blank space and reported a height that included it. Such a box is now laid out again where it lands, so its internal spacing closes up and it becomes shorter — which can move the content after it, and occasionally fit one more line on a page. See [Forward compatibility](#forward-compatibility).
