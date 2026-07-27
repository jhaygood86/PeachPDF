# A line inside the boundary tolerance belongs to one page

Issue [#446](https://github.com/jhaygood86/PeachPDF/issues/446), under
[#400](https://github.com/jhaygood86/PeachPDF/issues/400).

## What was wrong

One membership question, two tolerances. `CssRect.WouldStraddleFragmentainer` keeps a line whose bottom
overhangs its band by up to `HtmlContainerInt.PageBoundaryEpsilon` (0.5pt) — it fits, so layout does not move
it — while `FragmentEmitter.FragmentRegion.Contains` counts `BandOverlapEpsilon` (1e-6) of that same overhang
as membership of the *next* band. A line ending anywhere in that 0.5pt window was therefore claimed by both
pages, and drawn a second time at the next page's `local.Y = MarginTop − (wordHeight − overhang)`, i.e. above
its content top. Found on `windows-latest` only, as `16 words claimed by [0,1], living in 0`.

## The fix

`FragmentEmitter.ClaimsWord` — the word arm of `BuildDraft` now asks `HtmlContainerInt.SlotStartingAt(rect.Top)`,
the same convention layout asked (`BandStartingAt(Top)`), so the two are one statement with one tolerance.

**It is a tie-break intersected with the region test, not a replacement for it, and that distinction is the
whole content of the change.** Replacing `region.Contains` outright was written first and is wrong twice:

- `PageGeometryTable.PageIndexOf` **clamps** everything above the first band's top into slot 0, so
  `SlotStartingAt` alone hands slot 0 every word a pass has not positioned yet — [#433](https://github.com/jhaygood86/PeachPDF/issues/433)
  arriving by a second route. Measured directly: the first emission of slot 0 froze **404** boxes where 100
  belong there.
- The region is also the *inline*-axis test that tells one multi-column column from another, which no
  page-grid slot index can speak to.

Intersecting them can only ever *remove* a claim, never invent one, which is exactly the property that keeps
the change out of the machinery described below. Fixed content is exempt from the tie-break: it repeats at
unshifted document coordinates in every slot, so the one slot its own Y falls in would name a single page
instead of all of them.

## What running it found that reading it did not

The outright replacement passed the whole suite (6760/6769) and still changed a showcase —
`box_decoration_break` page 5 gained a decoration strip at its bottom belonging to the *next* page's line. The
chain, established by logging `EmitSlot`/`HoldsFragmentsFor` rather than by reading: extra slot-0 claims →
more boxes in `FragmentEmitter._frozen` → `HtmlContainerInt.InvalidateEmittedFragmentsFor` now fires on a
later reposition (3 × `InvalidateFrom(0)` where main fires none) → slots 0–4 are emitted **again** at
`Finish()`, from *final* geometry, so slot 4 picked up a line box laid out two passes after it was first
frozen. See
[.claude/invariants/fragmentation-which-drafts-exist-decides-whether-a-frozen-slot-is-emitted-again.md](../invariants/fragmentation-which-drafts-exist-decides-whether-a-frozen-slot-is-emitted-again.md).

The final rule changes **zero pixels and zero page counts across all 69 showcases** (PDFium, per-page pixel
comparison rather than a byte hash). That is the expected result, not a null one: the duplicate always lands
in the following page's top margin, where the page clip hides it — the damage is to the text layer
(copy/paste, search, content-stream size), which is what the fixtures assert.

## The fixture, and why it is not a Windows fixture

`BandMembershipToleranceTests` lays the document out **twice**: once to measure where the first page's last
line actually falls, then again on a band shortened by (slack + 0.25pt) so that same line overhangs by a
quarter point. That lands inside the window whatever the platform's font metrics are, and the helper asserts
`Assert.InRange(overhang, 1e-6, PageBoundaryEpsilon)` before asserting anything else, so it can never
degrade quietly into a second copy of the ordinary case. Reproduces the exact `by [0,1], lives in 0`
diagnostic on Linux.

Load-bearing check: neutralising `ClaimsWord` back to `region.Contains` fails 4 tests (3 theory cases +
`TheOverhangingLine_IsClaimedByThePageItsTopStartsIn`) and nothing else.

## Deliberately not done

- **The `Lines` arm has the same defect and is left alone** — a decoration rectangle is a taller thing than a
  word rectangle and can straddle by far more than the tolerance, so "the band its top starts in" is the wrong
  answer for it. Filed separately; see the issue linked from
  [.claude/accepted-gaps/decoration-rectangles-claimed-by-two-fragmentainers.md](../accepted-gaps/decoration-rectangles-claimed-by-two-fragmentainers.md).
- **`WouldStraddleFragmentainer`'s page arm still names its band from the page grid** ([#435](https://github.com/jhaygood86/PeachPDF/issues/435)).
  Nothing here needed it, and its blocker looks no different from here: the two bands still disagree wherever
  block flow leaves content in a later band without recording a break.

## Evidence

Suite 6760 passed / 6769 total (net8.0), CLI 96/96, `dotnet build PeachPDF.slnx -t:Rebuild` 0 warnings,
changed lines covered 514,273 times, 69/69 showcases pixel- and page-count-identical.
