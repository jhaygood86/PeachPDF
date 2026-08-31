# Per-page reflow now fires on size-only overrides, and a named page's opening content measures correctly

**Landed:** 2026-08-31 — Convergence loop bug fixes and gate widening (Layer C, mixed page orientation/size support)
**Doc section:** docs/html-css-support.md § Known boundaries of per-page margins / § `size` property
**Verified against v0.9.15:** `git show v0.9.15:docs/html-css-support.md` confirms the prior wording ("A block spanning a page boundary keeps its start-page measure") and confirms `size` had no per-page/independent-page concept at all at that tag — both are genuine changes since 0.9.15, in scope for the next release notes.

Two independent fixes to the per-page horizontal reflow machinery introduced by the mid-fragment
block/text rewrap work:

1. **A document that mixes only physical page sizes (no margin override at all) now reflows.**
   Previously, `HtmlContainerInt.UseVariablePageWidth` (renamed `UseVariableInlineMeasure`) only
   fired when some `@page` rule overrode a left/right margin — a document whose per-page rule
   changed only the sheet `size` (e.g. a landscape named page for a wide table, with the same
   margins everywhere) got its own independent physical PDF page, but its content still wrapped
   at the document's base measure rather than that page's own (wider or narrower) one. Content on
   a size-only-overridden page now re-wraps to that page's own measure, exactly as a margin
   override already did.

2. **A named page's own opening content now measures correctly the first time.** A box that
   opens a named page (`page: <name>`) used to be measured against the page-geometry slot's
   *previous* state — its own placement registers the new name (which invalidates that cached
   geometry) moments *after* its width was already resolved against the stale, pre-registration
   value, and nothing downstream re-checked it. Concretely: a paragraph whose `page: wide` opened
   a page with much wider margins than the page before it could silently keep the *narrower*
   previous page's measure. This is fixed; the box's own content now genuinely uses the named
   page's own margins/size from the moment it opens.
