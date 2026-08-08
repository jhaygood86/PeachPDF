# A hyphenated word split at a page/column boundary no longer keeps an unnecessary hyphen

**Landed:** 2026-08-08 — A hyphenation split made for one fragmentainer's measure survives into the resumed pass (issue #344)
**Doc section:** none — the previous behavior was never described in `docs/html-css-support.md`'s `hyphens` coverage; there is nothing to correct there.
**Verified against v0.9.8:** `git show v0.9.8:src/PeachPDF/Html/Core/Dom/CssLayoutEngine.cs` shows `TryHyphenateWord`'s prefix/suffix split with no linkage back to the original word and no undo path in `CreateLineBoxes`'s discard branch — the defect predates the tag.

With `hyphens: auto` (or an explicit soft hyphen), a word whose hyphenation split happened to fall on
the exact line a page or column break discarded could show a needless hyphen on the following page —
the split (prefix ending in `-`, suffix continuing it) was computed against that now-abandoned line's
own remaining width and was carried forward unchanged, even though the resumed page's fresh, undivided
width could fit the whole original word without splitting it at all. A document relying on automatic
or soft-hyphen hyphenation near a page or column boundary should now see that word re-hyphenated (or
left whole) against the width it actually lands on, rather than keeping a stale, unnecessary hyphen.
