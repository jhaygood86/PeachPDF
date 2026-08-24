# Flexbox stretch/shrink-to-fit sizing ignores `box-sizing: border-box`

_Tracked as [#815](https://github.com/jhaygood86/PeachPDF/issues/815). Left out of scope by
[#811](https://github.com/jhaygood86/PeachPDF/issues/811), which fixed the identical bug for CSS Grid._

`CssLayoutEngineFlex.ResizeItem` (main-axis stretch) and `CssLayoutEngineFlex.ShrinkColumnItemToContentWidth`
(cross-axis shrink-to-fit under `flex-direction: column`) each compute the value they assign to a flex
item's `box.Width`/`box.Height` by unconditionally subtracting the item's own padding+border from its
allotted outer size, then assigning that content-space number directly. That's only correct for
`box-sizing: content-box` — per this engine's own box-sizing contract
(`CssBox.ActualBoxSizeIncludedWidth`/`ActualBoxSizeIncludedHeight`, `CssBox.StyleProperties.cs`), a
`border-box` item's `Width`/`Height` string must hold the full outer size instead. A stretched or
shrink-to-fit flex item using `box-sizing: border-box` with non-zero padding/border currently renders
smaller than intended, by exactly that padding+border amount — [CSS Box Sizing Module Level 3
§2](https://www.w3.org/TR/css-sizing-3/#box-sizing).

#811's fix touched the shared `ItemContentCommit.CommitLayout` (the final, real-page-grid layout pass
both `CssLayoutEngineGrid` and `CssLayoutEngineFlex` route through), which prevents that shared code
path from compounding the error on top of what `ResizeItem`/`ShrinkColumnItemToContentWidth` already
got wrong — but does not fix those two flex-specific call sites themselves. Closing this gap is
flex-engine work (mirroring the `PlaceItemInCell`/`MeasureItemHeight` fix #811 made to
`CssLayoutEngineGrid.cs`) distinct from the Grid fix, so it was filed as a follow-up rather than folded
into that PR.
