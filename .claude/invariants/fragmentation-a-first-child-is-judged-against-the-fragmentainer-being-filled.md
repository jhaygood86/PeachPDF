# A first child is judged against the fragmentainer being filled

`CssBox.ResolveBlockChildOffset` decides whether a child's collapsed top margin crosses a page boundary
(css-break-3 §5.2) from its starting point, `baseTop`: the previous sibling's bottom, or for a first child
the parent's content top. The band that point *ends in* (`BlockConstraint.EndingAt`) is not always the one
the pass is filling. When the parent's own top padding or border has already crossed the foot, the parent's
content top is on the next page, so the child's margin crosses nothing in that band, and the child used to be
placed there while the pass still filled the previous page. The pass then broke before the child's first
line, and the next pass started that line at the page top, above the child's own box.

So a first child whose `baseTop` already falls past the live fragmentainer's band (`CurrentFragmentainer`)
takes the break before it (`RequestBreakBefore(filling.BandBottom)`), and the parent, left with nothing on
that page, moves whole. This does not depend on the child having a top margin, or on the child being the
parent's direct first child: a chain of first children each judge the same edge. A version of the rule that
also required a margin (`top > baseTop`) fixed only the case a heading's margin made obvious and left a
zero-margin paragraph, a padded inner wrapper and a leading edge taller than the page misplaced.

**Measured symptom:** a line drawn above its own box after a page break (visible as text above its
background, a border bar drawn into the page margin, or a heading highlight missing; see
`FirstChildAtThePageFootIntegrationTests`), and in a box that clips to its fragment (an auto-height
`overflow: hidden` block) the whole first line missing. Any other question asked from `baseTop` about "the
band being filled" has the same trap: ask the live fragmentainer, not the band the edge ends in.
