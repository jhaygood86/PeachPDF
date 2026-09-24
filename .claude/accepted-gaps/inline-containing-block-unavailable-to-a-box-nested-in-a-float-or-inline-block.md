# An inline containing block is unavailable to an absolutely positioned box nested in a float or inline-block (#1304)

**Gap:** CSS 2.1 §10.1 (4.1) forms the containing block of an absolutely positioned box from its
positioned inline ancestor's padding boxes, and CSS Positioned Layout 3 §2.1 from its first and last
fragments for a wrapped inline. `DomUtils.InlineContainingBlockOf` does that from the inline's
`Rectangles`, which only exist once `CreateLineBoxes` has finalized the lines.

A box that sits directly in the inline's flow is deferred until then (`CssLineBoxCoordinates.
AbsolutelyPositioned`). A box nested inside a float or a block-content inline-block *inside* that
inline is laid out as part of that box's content, mid-flow. By then `FlowBox` has already reset the
inline's rectangles, so `InlineContainingBlockOf` returns null. The callers then fall back to the
inline's line-local `Location` (0, 0), and the box lands at the sheet origin.

Probed: `<span style="position:relative">aa<span style="float:left">f<b style="position:absolute;
top:0;left:0">` puts the `<b>` at (0, 0) while the span's fragment starts at X≈155. An inline-block
holding block content (`<span style="display:inline-block"><div>f<b …>`) is the same. An inline-block
holding inline content is not at the origin, but it is still wrong: the `<b>` lands at the
inline-block's own left edge rather than the span's start, which is where Chrome puts it. Measured in
review: (125.7, 39.6) against Chrome's (112.5, 39.8); one probe here put it at X=145.9 with the span
starting at 134.7. The difference is the width of the span's text before the inline-block.

Unchanged from `main` before #1299: probed there with the same markup, both shapes also put the `<b>`
at (0, 0), because the parser never split the inline around a float and the inline's `Location` was
read the same way.

Not the same as an **empty** positioned inline (one holding nothing but the absolutely positioned box).
That shape has no fragment at all, ever, and is handled: `CssLayoutEngine.EmptyInlineContainingBlockFor`
gives it a zero-width containing block at the box's place on the line. It deliberately declines an
inline outside the flow being finalized (`IsSelfOrDescendantOf(ancestor, line.OwnerBox)`), which is
exactly this gap's shape: without that guard the float's inner line was taken for the inline's line and
the box was anchored at an arbitrary point inside the float, which the float's own later placement then
did not move (the box escapes the float's translation, `CssBox.EscapesTranslationOf`).

Related edge: a box laid out on a pass that stops at a page break sees only the fragments placed so far,
so its `bottom`/`right` resolve against the last fragment on that page.

**Why out of scope:** it predates #1299 and is unchanged by it. Fixing it needs either deferral across
the float/inline-block's own content layout (up to the outer flow's `CreateLineBoxes`) or a
re-positioning pass after the flow, and both reach into paths #1299 did not otherwise touch.
