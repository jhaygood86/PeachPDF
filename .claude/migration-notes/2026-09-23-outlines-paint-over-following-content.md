# Outlines now paint over the content that follows them

**Before.** An outline was drawn as soon as its own box finished painting. Content painted later in
the same stacking context covered any part of the ring that spilled outside the box, such as the
next sibling's background and text. A thick or offset outline could look cut off, or show the
following text through it.

**Now.** Outlines are drawn on top of the neighbouring in-flow content, floats, and
`z-index: auto`/`0` positioned boxes in their stacking context. They are still drawn under
positive-`z-index` content, so a raised overlay covers them as it does in browsers. A positioned box, float, inline-block, or flex or grid
item draws the outlines inside it before later content paints. In tagged PDF output, outlines are
marked as artifacts.

**Why.** CSS Positioned Layout 4's painting order lets outlines be drawn late in the stacking context
so they stay visible, and browsers draw them over following content.
