# Outlines now paint over the content that follows them

**Before.** An outline was drawn as soon as its own box finished painting. Content painted later in
the same stacking context covered any part of the ring that spilled outside the box, such as the
next sibling's background and text. A thick or offset outline could look cut off, or show the
following text through it.

**Now.** Outlines are drawn on top of the neighbouring in-flow content, floats, and
`z-index: auto`/`0` positioned boxes in their stacking context. They are still drawn under
positive-`z-index` content, so a raised overlay covers them, as in Chromium (Firefox draws outlines
over positive-`z-index` content too). Unlike Chromium, `z-index: auto`/`0` positioned boxes paint under
the outline rather than over it, so a following `position: relative` sibling cannot cover a ring. A
positioned box, float, inline-block, or flex or grid item draws the outlines inside it before later
content paints. In tagged PDF output, outlines are marked as artifacts.

**Also changed.** A positive-`z-index` child now paints over its parent's collapsed table borders and
list marker. Before, a raised child of a `border-collapse: collapse` table or a list item that is a
stacking context was drawn *under* the table's collapsed borders and the item's marker, contrary to
CSS 2.1 Appendix E, which paints positive-`z-index` layers last.

**Why.** CSS Positioned Layout 4's painting order lets outlines be drawn late in the stacking context
so they stay visible, and browsers draw them over following content.
