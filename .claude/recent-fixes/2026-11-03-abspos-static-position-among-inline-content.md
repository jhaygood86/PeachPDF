# An absolutely positioned box among inline content sits at its place on the line (#2069)

**What was missing.** `StaticPositionIn` returned null for a box with inline in-flow siblings, so
`aaa<span style="position:absolute">` landed at the containing block's corner.

**Idea.** The flow already sets such a box aside at the cursor (`SetAsideBox`) and already has `PlaceOnFinalLine`
(the place carried through `text-align`, justification and bidi reordering by an anchor word, written for the empty
inline containing block, #1309). `LayoutAbsolutelyPositioned` now stores `(PlaceOnFinalLine, line top)` on the box
(`InlineStaticPosition`) for the duration of its layout and `StaticPositionIn` returns it for an axis whose offsets
are both `auto`. Cleared afterwards, since the line can still be moved (table cell alignment).

**Not done.** `direction: rtl` (static position from the right edge) - see the accepted-gap file. The block position is
the line's top; browsers agree for line-height larger than the box, not verified against every vertical-align case.

**Evidence.** Four new `AbsoluteStaticPositionTests` (after a word, right-aligned line, later wrapped line, one-axis
offset kept); full engine and PeachPDF suites green.

**Follow-up: `direction: rtl` (#2153).** `StaticPositionIn` now also answers for an rtl parent: with `left`/`right` both `auto` the box's right margin
edge sits at the static position (the parent's content right edge, or the place on the line). The width is unknown when the box is placed, so
`CommitBlockChildOffset` records the edge (`StaticRightEdge`) and the epilogue shifts the box once its width is final, beside the existing `right`
fallback. The accepted gap is closed and its file deleted.
