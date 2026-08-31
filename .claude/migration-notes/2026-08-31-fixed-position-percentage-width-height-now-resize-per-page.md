# A `position: fixed` box's percentage `width`/`height` now resize per page

**Landed:** 2026-08-31 — Fixed-position per-page size resize (Layer K Tier 1, mixed page orientation/size support)
**Doc section:** docs/html-css-support.md § Known boundaries of per-page margins
**Verified against v0.9.15:** `git show v0.9.15:docs/html-css-support.md` confirms the prior wording ("`position: fixed` elements... keep positioning against the base page box on margin-overridden pages") with no mention of size resolving per page at all — a genuine change since 0.9.15, in scope for the next release notes.

Previously, a `position: fixed` box's percentage `width`/`height` resolved once, globally, against the
document's base page area — on a document that mixes physical page sizes (e.g. a landscape named page
alongside a portrait base), the box's own size stayed the same on every page regardless of how much
wider or narrower that page's own area actually was. A percentage `width`/`height` now resolves against
each page's own area, matching how a percentage `left`/`top` already does (previous layer). This
resizes the box's own frame — background, border, clip, and any replaced content (`<img>`, an inline
`<svg>`, which has no wrapping algorithm to invalidate) — correctly on every page. An absolute-length
`width`/`height` is unaffected either way, since it never depended on the page's own area to begin with.

**What does not change**: the box's own internal content (text/child elements) is laid out exactly once
and is not re-flowed to the new size — per [CSS Position 3 §3](https://www.w3.org/TR/css-position-3/#fixed-positioning),
a fixed box's content must not be paginated, so this is the spec-correct behavior rather than a
remaining gap. A non-replaced fixed box with real text content and a percentage size may show visible
slack or overflow its resized frame on a page whose measure genuinely differs from the base one.
