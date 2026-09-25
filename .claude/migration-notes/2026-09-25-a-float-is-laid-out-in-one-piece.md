# A block-level float is laid out in one piece, and moves whole when it fits on a page

**Before (v0.9.19):** a `left`/`right` float placed among block-level siblings broke between its lines
at a page boundary. Its break ended the layout pass, so the in-flow content laid out beside it was placed
back on the page the break left, which was already emitted:

- a block beside a float taller than a page was drawn only from where the float ended; its earlier
  lines were on no page (and the first one drawn sat above the page's top margin);
- text after a float that crossed a boundary could be lost, including the paragraph after it;
- when a float moved to the next page, the following block's boundary line was drawn twice, at the
  foot of one page and above the next page's top margin.

**Now:** the float's content is laid out in one piece, as a float among inline content already was. One
that would straddle a page boundary but fits on a page moves whole to the top of the next page, and the
content after it flows around it there; a later float is placed no higher than it, and an `inside`/`outside`
float takes the new page's side of the spread. One taller than a page runs on across pages, each page showing
its slice, while the text beside it keeps flowing around it: a line of the float that straddles a page
boundary is cut, part on each page, rather than moved. A forced break inside the float
(`break-before: page` on one of its children) is no longer honoured. A float that holds a multi-column
container, and a float inside a column of one, keep the old behaviour.

**Why:** CSS Fragmentation 3 §4.4 (content must not be lost) and CSS 2.1 §9.5 (line boxes beside a float
keep flowing around it). Tracked as #1339 and #1340; full float fragmentation is #317.

Confirmed against `v0.9.19`: `CssBox.LayoutBlockChild` was a bare `PerformLayoutImp` call with the
fragmentainer attached, and `CssLayoutEngine.LayoutContentUnbroken` did not exist, so neither path laid a
float out unbroken. #1348, after v0.9.19, did so for a float among inline content (`FlowFloatChild`); a
floated block child was first laid out unbroken by this change.
