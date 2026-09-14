# `BoundsEndAtItsContent` has two reasons, and they do not extend alike

`FragmentEmitter.Draft.BoundsEndAtItsContent` is set by two unrelated situations, and `ExtentOf`'s
bottom extension must not treat them as one:

1. **A captured instance still continuing through a nested fragmentainer**
   (`CapturedInstance.Continuing`). Its own bounds have not had a height applied yet, so they describe
   *nothing* about this fragment. Everything it holds here — inline content included — is all there is
   to measure it from.
2. **The page-grid/issue-#569 arm** (`boundsEndAtContentOnThePageGrid`): the box's bounds are its real,
   finished ones, and the extension only exists to correct a height an item-content commit pass pinned
   before the overflowing content was known. Here the box's own bounds already record its line boxes,
   so an inline-level child's own box — the font's *content area*, which is taller than the line box
   whenever `line-height` is shorter than the font — must **not** extend it. Counting it re-grows a
   block's painted border box back to the glyph ink and silently undoes a short `line-height`.

`Draft.BoundsStatedByACapturedContinuation` carries the distinction; `ExtentOf` skips inline-level
in-flow children only when it is false.

**The measured symptom if you collapse the two back together.** Skipping inline-level children for
*both* reasons fails exactly 11 tests, every one a multi-column or `box-decoration-break` case:
`FragmentEmitterTests.ABoxSplitAcrossColumns_*` (4),
`BoxDecorationBreakLayoutIntegrationTests`/`BoxDecorationBreakPaintIntegrationTests`'s
`*AtAColumnBoundary*` (6), and
`StraddlingListMarkerTests.AnItemCrossingAColumnBoundary_KeepsItsMarkerInTheColumnItBeginsIn`. A
continuing column fragment collapses to a zero/short rectangle, so its decoration is drawn at the
wrong height or not at all. Conversely, *not* skipping them for the page-grid arm is invisible to the
whole suite and only shows up in a rendered raster — the band behind a `line-height: 0.75` block
measures the font's height instead of the declared one.

Both directions are pinned:
`LineHeightLineBoxExtentTests.ShortLineHeight_PaintsItsBackgroundOverTheLineBox_NotTheOverflowingGlyphs`
for the page-grid arm, and the column tests above for the capture arm.

`AssertSameDraft` compares the new field alongside `BoundsEndAtItsContent` — it is read at
materialization, and that oracle is deliberately exhaustive over everything materialization or paint
can see.
