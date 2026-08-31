# A straddling main-column paragraph's continuation lines now re-wrap to their own page's measure

**Landed:** 2026-08-31 — Mid-fragment block/text rewrap (Layer D, mixed page orientation/size support)
**Doc section:** docs/html-css-support.md § Known boundaries of per-page margins / per-page horizontal reflow
**Verified against v0.9.15:** `git show v0.9.15:docs/html-css-support.md` confirms the prior wording ("a box spanning a page boundary keeps its start-page measure across its fragments") — confirmed genuine behavior change since 0.9.15, in scope for the next release notes.

Previously, when an auto-width main-column paragraph (or other block holding direct text/inline
content) started on one page and continued onto a later page whose own `@page` margins or size
differ, its whole flow was laid out once at the *start* page's measure — a continuation line on a
narrower later page could extend past that page's own content edge (clipped/overlapping at paint
time) rather than re-wrapping to fit it. Text now genuinely re-wraps line-by-line at each page's
own content width as the flow crosses onto it (css-break-3 §5.1), matching what CSS Paged Media 3
already promises for content between page breaks — `text-align: left/right/center/justify` all
flush against each line's own (per-page) measure, so alignment stays correct across the straddle
too. The block's own outer border box (its background/border rectangle, and any non-text content)
does not yet resize per page — only the text wrapping inside it does; tracked as
[#876](https://github.com/jhaygood86/PeachPDF/issues/876). Tables, flex/grid containers, and
multi-column containers are unaffected by this change — their own content re-wrap is separately
tracked ([#197](https://github.com/jhaygood86/PeachPDF/issues/197),
[#196](https://github.com/jhaygood86/PeachPDF/issues/196),
[#198](https://github.com/jhaygood86/PeachPDF/issues/198)).
