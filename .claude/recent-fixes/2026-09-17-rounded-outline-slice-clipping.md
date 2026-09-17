# Rounded sliced outlines clip only at fragmentation breaks

Rounded `box-decoration-break: slice` geometry paints against the unbroken strip and normally clips
back to each fragment rectangle. Reusing that rectangle unchanged for outlines clipped away the part
of the outline outside the border box. The all-edge guard in `OutlineDrawHandler` also suppressed the
surviving real start and end curves.

## The load-bearing distinction

A sliced outline needs two boundaries at once: fragmentation breaks cut exactly at the fragment
rectangle, while real box edges retain the room needed for the outline to spill outward. The outline
handler now derives that clip from its already-computed outer rectangle and the four physical-edge
flags, so `FragmentPainter` does not duplicate outline width, offset, auto-style, or negative-offset
clamping rules.

Rounded radii are passed to the shared edge painter for partial edge sets as well as complete rings.
Its existing physical-edge geometry draws curves only at the element's true corners and square ends
at internal breaks. A deeply negative offset can move the outline wholly beyond a short slice; an empty
intersection is skipped before reaching the PDF backend rather than represented as a negative clip
rectangle.

## Evidence

`RoundedOutlineOnAWrappingInlineElement_ClipsOnlyAtBreakEdges` verifies the ordered clip and path calls
for all three lines. `RoundedOutlineOnAWrappingInlineElement_EmptyInsetSlicePaintsNothing` covers the
empty-intersection case through both the recording adapter and real PDF serialization. The outline
showcase includes the ordinary wrapped inline case for raster verification.
