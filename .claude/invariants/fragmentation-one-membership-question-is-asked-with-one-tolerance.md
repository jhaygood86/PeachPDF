# One membership question is asked with one tolerance

_CSS Fragmentation Level 3. Tracker: [#320](https://github.com/jhaygood86/PeachPDF/issues/320)._

"Which fragmentainer does this content belong to?" is asked twice — once by layout, when it decides whether to
keep a line or move it on (`CssRect.WouldStraddleFragmentainer`), and once by the emitter, when it decides
which fragment holds it (`FragmentEmitter.ClaimsWord`). **Both must use `PageBoundaryEpsilon`, via
`SlotStartingAt`/`SlotEndingAt`.** They did not: layout tolerated an overhang of up to 0.5pt while the emitter
treated 1e-6 of the same overhang as membership of the next band, so every line ending inside that window was
claimed by two pages and painted twice — the second time above the following page's content top
([#446](https://github.com/jhaygood86/PeachPDF/issues/446); measured on `windows-latest` as
`16 words claimed by [0,1], living in 0`).

A second tolerance for the same question is not a safety margin, it is a disagreement waiting for a document
to land in it. If a new site needs to ask which fragmentainer something is in, it asks
`SlotStartingAt`/`SlotEndingAt` — not a fresh epsilon, and not a raw overlap.

**The grid's answer alone is not the whole test, though.** `PageGeometryTable.PageIndexOf` clamps everything
above the first band's top into slot 0, so `SlotStartingAt` asked on its own gives the first page every word a
pass has not positioned yet ([#433](https://github.com/jhaygood86/PeachPDF/issues/433)'s defect, by another
route — measured at 404 boxes frozen into slot 0's first emission where 100 belong). Keep it as a tie-break
intersected with the region/overlap test, so it can only remove a claim and never invent one.
