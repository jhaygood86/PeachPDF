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

**The grid's answer alone is not the whole test, though**, and both ways it falls short cost a defect.

`PageGeometryTable.PageIndexOf` clamps everything above the first band's top into slot 0, so `SlotStartingAt`
asked on its own gives the first page every word a pass has not positioned yet
([#433](https://github.com/jhaygood86/PeachPDF/issues/433)'s defect, by another route — measured at 404 boxes
frozen into slot 0's first emission where 100 belong). Keep it as a tie-break intersected with the
region/overlap test, so it can only remove a claim and never invent one.

**And the tie-break applies only where layout was actually *asked*.** It is a statement about a decision
layout made — "this line fits, keep it" — so it is meaningless where no such decision was taken. Two cases
used to reach it this way: a flex or grid item's content laid out under `SuppressWordPageBreaks` and never
revisited when the engine translated it; and `MonolithicContent.FitsNoFragmentainer`, which leaves anything
taller than the band exactly where it is. Both left a line overhanging by many points with no fragmentainer
of its own to be whole in. The first is now closed at the source — `CssLayoutEngineGrid`/`CssLayoutEngineFlex`
commit their items'/lines' content live at its final position, so that straddle check runs for real and this
arm has nothing left to do for it. The second was, for a time, handled by keeping the claim in **both** bands
— the earlier one showing the sliver that fits, the later one the remainder, since applied unconditionally
the tie-break had deleted the content outright: 45 words, one line per break, on a four-page flex document
([#477](https://github.com/jhaygood86/PeachPDF/issues/477)). `HtmlContainerInt.FallsPast` is the "layout could
not fix this" test, in the same tolerance — gate on it rather than adding a third epsilon.

**That second-claim behavior for `FitsNoFragmentainer` is itself now retired**
([#484](https://github.com/jhaygood86/PeachPDF/issues/484)): content that fits no fragmentainer is clipped to
the first one it starts in rather than repeated in every later one it geometrically overlaps.
`FragmentEmitter.ClaimsWord`'s tie-break gates the extra claim on `!MonolithicContent.FitsNoFragmentainer`
(same rect height, same `container.PageSize.Height` threshold — no new tolerance) so it grants an extra claim
only where content *could*, in principle, have fit some fragmentainer and simply wasn't moved there — which,
with the flex/grid case closed too, is no case at all today. The arm stays rather than being deleted, both
because a future case could still reach it and because `FallsPast` is deliberately looser than layout's own
check here (it drops `MonolithicContent.ClonedBlockInsets`' bottom inset and asks the page band even inside a
column), so removing it outright would be removing a safety margin, not just dead code.

Note what did *not* catch the original defect: "every word claimed exactly once" is **satisfied** by the
loss, because the word is still claimed once, by the page that can only show a sliver of it. Nor did the
showcase pixel diff — the 0.5pt-window duplicate lands in the next page's *top margin*, where the page clip
hides it, while the straddle-beyond-tolerance copy lands inside the content area and no showcase has one. A
membership rule needs a fixture on **both** sides of "could layout have moved this?" — and, for the
`FitsNoFragmentainer` case specifically, a rasterization check: a token-presence test cannot tell a clipped
line from a duplicated one, only a rendered page can (see
[testing conventions](../../CLAUDE.md#testing-conventions)).
