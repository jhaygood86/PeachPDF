# A repeating `<tfoot>` now closes every page a table covers

**Landed:** 2026-07-28 (83f01e00) — Close every page a table covers with the footer it repeats (#513)
**Doc section:** docs/html-css-support.md § [Page Breaks](../../docs/html-css-support.md#page-breaks)
**Verified against v0.9.6:** this note was not present in the `v0.9.6` tag's docs — confirmed genuine behavior change since 0.9.6, in scope for the next release notes.

A repeating `<tfoot>` used to be drawn exactly once on a table whose row continued mid-cell — under the table's last row, and nowhere else. It now closes every page such a table covers, and the room it takes at the foot of each of those pages is left free, so a cell's text stops above it instead of flowing into the space it occupies. A document of that shape therefore prints its footer on more pages, and may **gain pages**, since each of those pages now holds the footer's height less text than it did when nothing was drawn there. See [Forward compatibility](#forward-compatibility).
