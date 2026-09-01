# A straddling main-column block's own outer border box now resizes per page

**Landed:** 2026-08-31 — Draft.InlineExtentDeltaWidth, the fragment-tree contract change tracked as issue #876
**Doc section:** docs/html-css-support.md § Known boundaries of per-page margins / per-page horizontal reflow
**Verified against v0.9.15:** `git show v0.9.15:docs/html-css-support.md` confirms per-page horizontal reflow did not exist at all at that tag ("a box spanning a page boundary keeps its start-page measure across its fragments") — this change (and the mid-fragment text rewrap it builds on, already covered by a sibling migration note) is entirely new since 0.9.15, in scope for the next release notes.

Previously, once an auto-width main-column block's continuation lines began re-wrapping to each
page's own measure mid-flow (issue #143's mid-fragment block/text rewrap layer), the block's own
outer border box — its painted background/border rectangle, and any non-text content sized against
its own width — did not follow: it stayed pinned to whichever page originally placed it, even as
the text inside it visibly re-wrapped to a narrower or wider later page. A block with a background
color spanning a page whose margins or size differ from its start page would show that background
at the wrong width, disagreeing with the text now correctly wrapped inside it. The block's own
frame now resizes to match each page's own content-right edge on every fragment, using the exact
same eligibility test and formula its text already re-wraps with, so the two always agree. Tables,
flex/grid containers, and multi-column containers are unaffected by this change — their own content
re-wrap remains separately tracked ([#197](https://github.com/jhaygood86/PeachPDF/issues/197),
[#196](https://github.com/jhaygood86/PeachPDF/issues/196),
[#198](https://github.com/jhaygood86/PeachPDF/issues/198)).
