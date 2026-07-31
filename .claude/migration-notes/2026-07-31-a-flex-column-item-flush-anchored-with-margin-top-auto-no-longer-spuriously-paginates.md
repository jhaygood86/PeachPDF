# A flex-column item anchored with `margin-top: auto` no longer spuriously paginates

**Landed:** 2026-07-31 — fix a detached measurement pass taking a real page break
**Doc section:** none dedicated; the affected pattern (`display:flex; flex-direction:column` with an
explicit `height` close to the page height, and a `margin-top:auto`-anchored last item) is ordinary
CSS, not a documented PeachPDF feature.
**Verified against v0.9.7:** the bug is present at the `v0.9.7` tag — `git show
v0.9.7:src/PeachPDF/Html/Core/Fragmentation/BlockConstraint.cs` shows `For`/`EndingAt` gated only on
`HtmlContainerInt.HasRealPageGrid`, with no check of `CurrentFragmentainer`, and `git show
v0.9.7:src/PeachPDF.TestHarness/Program.cs` confirms the `invoice` showcase (added at that tag,
commit `eb23595`) already had this exact markup; `BlockConstraint.cs` was untouched between that tag
and the fix, so the same 2-page rendering this note describes was already present at `v0.9.7`.

A `display:flex; flex-direction:column` container whose declared `height` sits close to (but under)
a page's own height, with a last item pinned flush to the container's bottom edge via
`margin-top:auto`, could previously trip a spurious page break: if any of that item's own descendants
(reached through a nested flex/grid formatting context — a common shape for footer-style layouts, a
row of columns followed by a closing line) happened, purely as an artifact of an internal measurement
pass, to sit at a position straddling the true page boundary, PeachPDF would insert a real page break
there and continue that content onto a mostly-blank next page — even though the document's real
content fit within one page with room to spare. A document that relied on this now renders one page
shorter, with the previously-stranded content back on the original page. See the `invoice` showcase
(`src/PeachPDF.TestHarness/Program.cs`) for the shape this affected.
