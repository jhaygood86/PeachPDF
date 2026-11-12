# calc-size() with an intrinsic keyword basis on height/min-height/max-height (#2366)

Load-bearing idea: no second layout pass is needed. On the block axis all four keywords (`auto`, `min-content`, `max-content`,
`fit-content`) are the same number, the content height, and `CssLayoutEngine.ApplyHeight` already runs once content is complete
(`PerformLayoutEpilogue`) and is where every height is turned into the final `ActualBottom`. `ResolveCalcSizes` parks the keyword
value (the property stays `auto`/`0`/`none` during layout, so `ActualBottom` is the content bottom), and `ApplyHeight` calls
`CssBox.ResolveCalcSizeHeights` first, which substitutes `ActualBottom - Location.Y - ActualBoxSizeIncludedHeight` for `size`.

Trap: the basis must exclude padding and border (the included height), or `border-box` sizing double counts them.
Not done: a re-layout that re-cascades would re-park from the stylesheet value, same as the inline axis.
