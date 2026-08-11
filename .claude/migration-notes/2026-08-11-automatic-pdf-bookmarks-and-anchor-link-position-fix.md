# PDFs now get automatic bookmarks from headings, and `<a href="#id">` links land on the right spot

Previously: PeachPDF never populated a PDF's outline (`/Outlines`) at all — no document produced a
bookmark sidebar, regardless of heading structure, and there was no CSS-level control over one. Separately,
an `<a href="#id">` anchor link's named destination was computed with a Y coordinate in the wrong axis
(passed as a `/FitV` action's horizontal `left` parameter instead of `/FitH`'s vertical `top`), so clicking
an anchor link routed a reader to the correct page but not reliably to the correct position on it.

Now: `h1`–`h6` automatically generate a nested PDF outline (via the new `bookmark-level`/`bookmark-label`/
`bookmark-state` CSS properties — see [PDF Bookmarks (Outline) Support](../../docs/html-css-support.md#pdf-bookmarks-outline-support)),
with zero configuration required — a document with no headings produces no outline and no `/PageMode`
change, exactly as before. Any existing document containing at least one heading will now open with a
populated bookmark panel (PDF readers set `/PageMode /UseOutlines` automatically once an outline exists) —
authors who don't want this can set `bookmark-level: none` on `h1`–`h6` (or any subset) to suppress it.
`<a href="#id">` anchor links (and running-element links inside `@page` margin boxes) now land at the
correct vertical position on their target page, fixing what was previously an unreliable jump.
