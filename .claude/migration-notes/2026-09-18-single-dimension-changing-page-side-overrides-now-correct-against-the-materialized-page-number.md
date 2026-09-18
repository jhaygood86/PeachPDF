# A `:first`/`:left`/`:right` override that changes only ONE of a page's width/height now corrects against the materialized page number too

**Landed:** 2026-09-18 — Correct single-dimension-changing page-side geometry against the materialized page number (issue #1041)
**Doc section:** docs/html-css-support.md § [Known boundaries of per-page margins](../../docs/html-css-support.md#page-rule)
**Verified against v0.9.18:** the `v0.9.18` tag's docs read "When content-empty pages are skipped (see pagination), `:first`/`:left`/`:right` resolve against the underlying page sequence, not the renumbered output pages" (unconditional) — confirmed genuine narrowing since 0.9.18, in scope for the next release notes.

A content-empty gap (e.g. a very tall empty element) earlier in a document can shift a later page's
*materialized* (printed) page number away from its raw position in the page grid. Previously, once a
`:first`/`:left`/`:right` `@page` rule changed that page's own content-box width **or** height (an
asymmetric `:left`/`:right` margin override, or any `size` override), the page's geometry always kept
resolving against its raw grid position instead of its materialized number — regardless of which single
dimension changed.

That page now gets a second, corrected layout pass instead, as long as the change is confined to a
single dimension (width alone, or height alone): its margins/sheet size are re-resolved against its
materialized number, and its content re-wraps/re-paginates against the corrected size. A document
relying on the old (grid-numbered) geometry for this specific combination — a dimension-changing
`:first`/`:left`/`:right` override *and* a preceding content-empty gap — will see that page's margins,
sheet size, and possibly its content's line-wrapping change to match the corrected, materialized-number-
appropriate geometry.

Two narrower cases are unaffected and keep the old (grid-numbered) behavior: a rule that changes **both**
the width and height at once, and any page under an active named page (`page: <name>`). A document with
no content-empty gap, or none using `:first`/`:left`/`:right` at all, is unaffected either way. See
[Out of scope / accepted gaps](../accepted-gaps/left-right-page-geometry-vs-materialized-numbering.md)
for what still remains open, tracked as
[issue #1041](https://github.com/jhaygood86/PeachPDF/issues/1041).
