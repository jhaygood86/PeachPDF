# A fixed box's containing block, and where presentational hints sit in the cascade

Both found by putting the SVG-reuse showcase side by side with a browser rendering it: the logo was
drawn too big, and the full-width rule under it stopped short of the right margin.

## Presentational hints were applied after the author sheet

`DomParser.AssignCssBlocks`' cascade ran UA → author-normal → **TranslateAttributes** → inline →
important passes. So `width`/`height`/`bgcolor`/`align`/… outranked every author rule, and only an inline
`style` could beat them. The showcase's `<svg width="500" height="110" class="running-logo">` therefore
ignored `.running-logo { width: 365px }` and rendered 37% oversized.

HTML maps those attributes to declarations at the very start of the author origin with zero specificity,
so any author rule wins. Moving the call to just before the author-normal phase is the whole fix. It is
deliberately *after* `uaSnapshot` is captured, so `revert` in an author rule still rolls back to the UA
value rather than to a hint. Nothing in the suite depended on the old order — 11,493 tests passed
unchanged, which is worth knowing before assuming this is a risky area.

## A fixed box was anchored to the sheet but sized against the page area

`CssBox.CommitBlockChildOffset`'s fixed branch resolved `left`/`top` from `(0, 0)` — the sheet corner —
while `CssLayoutEngine.GetBoxWidth`, `GetBoxHeight` and the `left: 0; right: 0` fill all resolved against
`PageBandGeometry.BandWidth`/`BandHeight`, which is the page **area** (sheet minus margins). Both halves
cannot be right. The measured symptom was a `position: fixed; left: 0; right: 0` rule 523.28pt wide (the
correct measure) drawn from x=0 (the sheet edge), ending 36pt short of the right content edge.

Resolved toward the spec — [CSS 2.1 §10.1](https://www.w3.org/TR/CSS21/visudet.html#containing-block-details)
makes the page area the containing block in paged media, which is also what a browser does when printing.
The origin is now `HtmlContainer.MarginLeft`/`MarginTop` (layout space, where the content edge lives — see
`HtmlContainerInt.PageContentRightOf`, which is `MarginLeft + BandWidth`), and
`FragmentEmitter.ComputeFixedPageOffset` mirrors it with **that slot's own** resolved margins
(`geom.MarginLeftPt * ppp`), since per-page margin overrides are exactly what that correction exists for.

Ten tests encoded the old convention and were updated, but one of them changed *meaning*, not just
numbers, and that is the part to know:
`FixedAbsoluteOffset_StaysTheSameDeltaOnEveryPage_RegardlessOfSizeOverrides` asserted that `left: 80pt`
produced an identical rectangle on two pages with different margins. Under a page-area containing block
that identity is wrong: the box is 80pt into *each page's own area*, which is two different distances
from the sheet edge. The test now asserts that, renamed to
`FixedAbsoluteOffset_SitsAtTheSameOffsetIntoEveryPagesOwnArea`.

A second trap, in `FixedPercentOffset_ResolvesToEachPagesOwnArea_OnAMarginOnlyOverrideDocument`: its
`left: 50%; top: 50%` fixture stopped proving anything, because the centre of a page area with symmetric
margins *is* the centre of the sheet — so both pages agreed by coincidence and the `NotEqual` guard
failed. It uses 25% now, which has no such symmetry. Any future per-page fixture wants an asymmetric
offset for the same reason.

## Evidence

- Full suite on net8.0: 11,493 passing, only the two pre-existing `LineClampIntegrationTests` failures.
- Probe document (`@page { margin: 38px 48px 42px }`, A4) read out of the content stream: the fixed
  control box now emits `36 798.39 273.75 15 re` (content corner, was the sheet corner at `0 826.89`),
  and the `left: 0; right: 0` rule `36 717.39 523.28 1.5 re` — 36 to 559.28, exactly the measure.
- Showcase re-rendered through PDFium and MuPDF; its fixed offsets were retuned, since they had been
  authored against the old sheet-corner origin and an oversized logo.
