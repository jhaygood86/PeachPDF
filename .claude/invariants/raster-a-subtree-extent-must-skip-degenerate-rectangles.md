# A subtree's extent must ignore degenerate rectangles, or a bitmap grows to the page origin

`SubtreeExtent` unions the rectangles of a fragment, its lines, its words and its descendants to size the bitmap a region is rendered into.
An anonymous text box reports a `WholeBoxRect` of `(0, 0, 0, height)`: no width, at the origin. Unioned, that drags the extent to (0, 0).

**Symptom, measured:** the flatten region of a paragraph of translucent text was 151 x 246 pt at the top-left of the page instead of the
17 pt line it is; a CSS-filtered `<div>` containing text had the same oversized bitmap (correct pixels, several times the file size and
memory). **Rule:** `Add` skips any rectangle without positive width *and* height. A box that genuinely paints something has both.
