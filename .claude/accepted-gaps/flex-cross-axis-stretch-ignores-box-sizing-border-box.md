# Flex cross-axis `align-items: stretch` ignores `box-sizing: border-box`

_Tracked as [#832](https://github.com/jhaygood86/PeachPDF/issues/832). Found while fixing
[#815](https://github.com/jhaygood86/PeachPDF/issues/815) (`CssLayoutEngineFlex.ResizeItem`/
`ShrinkColumnItemToContentWidth`), itself the flex analog of [#811](https://github.com/jhaygood86/PeachPDF/issues/811)
(CSS Grid) - this is a third call site with the identical bug that neither #811 nor #815 named, so it was
deliberately left out of #815's fix._

`CssLayoutEngineFlex.ComputeCrossOffsets`'s `AlignItem.Stretch`/`AlignItem.Normal` branch (both the
row-direction height-stretch arm and the column-direction width-stretch arm) computes a stretched flex
item's cross-axis size by unconditionally subtracting the item's own raw padding+border from the target
cross size, then assigning that content-space value directly to `box.Height`/`box.Width`. That's only
correct for `box-sizing: content-box` - per this engine's own box-sizing contract
(`CssBox.ActualBoxSizeIncludedWidth`/`ActualBoxSizeIncludedHeight`, `CssBox.StyleProperties.cs`), a
`border-box` item's `Width`/`Height` string must hold the full outer size instead. A stretched (the
default `align-items`/`align-self` behavior) flex item using `box-sizing: border-box` with non-zero
padding/border currently renders exactly that padding+border smaller than its line's cross size, on both
axes - [CSS Box Sizing Module Level 3 §2](https://www.w3.org/TR/css-sizing-3/#box-sizing).

Closing this gap is the same transformation #815 applied to `ResizeItem`/`ShrinkColumnItemToContentWidth`
(subtract `ActualBoxSizeIncludedWidth`/`Height` instead of raw padding+border), applied to
`ComputeCrossOffsets`'s stretch branch instead - distinct code, so filed as its own follow-up rather than
folded into #815.
