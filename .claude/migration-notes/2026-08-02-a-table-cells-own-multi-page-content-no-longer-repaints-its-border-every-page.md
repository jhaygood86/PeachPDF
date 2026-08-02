# A table cell's own multi-page content no longer repaints its border/background on every page

**Landed:** 2026-08-02 — Walk a fan-out break token's per-child continuations in RecordChain (issue #590)
**Doc section:** docs/html-css-support.md § [Page Breaks](../../docs/html-css-support.md#page-breaks)
**Verified against v0.9.7:** `git show v0.9.7:src/PeachPDF/Html/Core/Fragmentation/FragmentEmitter.cs`'s
`RecordChain` only walks a `BlockBreakToken`'s linear `ChildToken` chain, with no case for
`TableBreakToken` (or `FlexBreakToken`/`GridBreakToken`/`FlexColumnBreakToken`) at all — confirmed the
defect this note describes was present at the last release, not introduced afterward.

A `<td>`/`<th>` (or a flex/grid item) whose own content genuinely spans more than one real page used to
have every one of its fragments treat both its top and bottom edge as the box's own — so with a visible
`border` or `background` the top edge repainted on every page the cell's content reached, rather than only
the page it actually opened on, and likewise for the bottom edge on every page but the last
(`box-decoration-break: slice`, the default). It now correctly slices: only the first fragment draws the
top border/background edge and only the last draws the bottom one, exactly as an ordinary block box
spanning pages already did. This did not affect a cell whose continuation was a content-free
continuation-shell fragment (a finished cell continuing with its row, or a `rowspan` cell's span crossing a
page boundary) — those were already correct; the gap was specific to a cell/item whose own content kept
producing genuinely new fragments pass after pass.
