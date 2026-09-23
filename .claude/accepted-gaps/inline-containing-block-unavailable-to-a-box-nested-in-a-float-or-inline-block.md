# An inline containing block is unavailable to an absolutely positioned box nested in a float or inline-block (#1304)

**Gap:** CSS 2.1 §10.1 (4.1) forms the containing block of an absolutely positioned box from its
positioned inline ancestor's first and last fragments. `DomUtils.InlineContainingBlockOf` does that
from the inline's `Rectangles`, which only exist once `CreateLineBoxes` has finalized the lines.

A box that sits directly in the inline's flow is deferred until then (`CssLineBoxCoordinates.
AbsolutelyPositioned`). A box nested inside a float or a block-content inline-block *inside* that
inline is laid out as part of that box's content, mid-flow. By then `FlowBox` has already reset the
inline's rectangles, so `InlineContainingBlockOf` returns null. The callers then fall back to the
inline's line-local `Location` (0, 0), and the box lands at the sheet origin.

Probed: `<span style="position:relative">aa<span style="float:left">f<b style="position:absolute;
top:0;left:0">` puts the `<b>` at (0, 0) while the span's fragment starts at X≈155. The inline-block
variant is the same.

Related edge: a box laid out on a pass that stops at a page break sees only the fragments placed so far,
so its `bottom`/`right` resolve against the last fragment on that page.

**Why out of scope:** before #1299 the whole shape was worse, because the split took the box out of the
inline entirely. Fixing it needs either deferral across the float/inline-block's own content layout or
a re-positioning pass after the flow, and both reach into paths #1299 did not otherwise touch.
