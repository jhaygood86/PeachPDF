# A first child's margin is judged against the fragmentainer being filled

`CssBox.ResolveBlockChildOffset` decides whether a child's collapsed top margin crosses a page boundary
(css-break-3 §5.2) from its starting point, `baseTop`: the previous sibling's bottom, or for a first child
the parent's content top. The band that point *ends in* (`BlockConstraint.EndingAt`) is not always the one
the pass is filling. When the parent's own top padding or border has already crossed the foot, the parent's
content top is on the next page, so the margin crosses nothing in that band, and the child used to be
placed there while the pass still filled the previous page. The pass then broke before the child's first
line, and the next pass started that line at the page top, above the child's own box.

So a first child whose `baseTop` already falls past the live fragmentainer's band (`CurrentFragmentainer`)
takes the break before it (`RequestBreakBefore(filling.BandBottom)`), and the parent, left with nothing on
that page, moves whole.

**Measured symptom:** a line drawn above its own box after a page break (visible as text above its
background), and in a box that clips to its fragment (`overflow: hidden`) the whole first line missing
(`MonolithicContentLayoutIntegrationTests.FirstChildOfAParentWhosePaddingCrossesThePageFoot_StartsItsFirstLineInsideItsBox`).
Any other question asked from `baseTop` about "the band being filled" has the same trap: ask the live
fragmentainer, not the band the edge ends in.
