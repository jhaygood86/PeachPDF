# An absolutely positioned box after page 1 is drawn, and no longer displaces the content after it

**Before (v0.9.19):** an absolutely positioned box with no positioned ancestor, reached after earlier
content had pushed layout past page 1, was drawn on no page. If it was the first child of its block, the
rest of that block was lost as well. If the content before it was monolithic (an image, an
`overflow: hidden` box), that block's content was instead drawn at the top of page 1, over what was
already there. Separately, any block whose first child was absolutely positioned started its in-flow
content below that child rather than at its own top.

**Now:** the absolute box is drawn on the page its offsets put it on (page 1 for the initial containing
block), and the content after it in the same block starts at the block's top, where it would be without
the absolute box. An absolutely positioned box's own content is laid out in one piece: one taller than a
page used to break between its lines, and every in-flow box after it in the same block was lost (drawn on
no page) when it was not its block's first child. It now runs on across pages with each page showing its
slice, and the content after it is kept. A box that is or holds a multi-column container still breaks
between its column lines as before, and as its parent's first child it still pushes the content after it
below itself. A line of the box that straddles a page boundary is cut, part on
each page, and an image, `break-inside: avoid` block or table inside it is sliced rather than moved or
broken between rows.

**Why:** CSS 2.1 §9.3.1: an absolutely positioned box is removed from normal flow and has no effect on
the layout of later siblings. CSS Fragmentation 3 §4.4: content must not be lost. Tracked as #1349.

Confirmed against `v0.9.19`: `DomUtils.GetPreviousSibling`'s end check omitted `PositionMode.Absolute`,
and `HtmlContainerInt` had no path re-opening an emitted fragmentainer for a newly placed box.
