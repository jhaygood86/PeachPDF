# `widows` now moves only the lines it needs, not the whole box

**Landed:** 2026-07-26 (1929b00b) — Move the lines widows asks for, not the whole box
**Doc section:** docs/html-css-support.md § [Page Breaks](../../docs/html-css-support.md#page-breaks)
**Verified against v0.9.6:** this note was not present in the `v0.9.6` tag's docs — confirmed genuine behavior change since 0.9.6, in scope for the next release notes.

A box that would have left fewer lines than `widows` after the break used to be moved to the next page in its entirety, leaving a gap at the foot of the page it came from. Only the lines it takes now travel, so such a paragraph keeps its first fragment where it was and simply ends a line or two earlier — which closes that gap and can change how much fits on the pages after it. See [Forward compatibility](#forward-compatibility).
