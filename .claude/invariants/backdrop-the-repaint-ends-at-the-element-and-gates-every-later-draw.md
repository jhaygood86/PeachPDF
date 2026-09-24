# A backdrop repaint stops at the element, so every draw after it must be gated, not just the child walk

`backdrop-filter` and transparency flattening rebuild "what was painted before this element" by walking the same fragment tree with a
second painter and stopping at the element (`_stopAt` for the backdrop, `_stopAfter` for flattening). Stopping the *child walk* is not
enough: a box paints things after its children too - collapsed table borders, its deferred outlines, its marker, its content image, its
raised (positive z-index) layers - and an ancestor of the element does all of that after the element's own paint returns.

**Rule:** once `_stopped` is set, `PaintFragment`, `PaintBoxContent`'s post-children steps, `DrawScopeOutlinesSoFar` and
`CloseOutlineScope` draw nothing. A new post-children step in `PaintBoxContent` needs the same `!_stopped` gate, or an ancestor's outline
or marker appears in the backdrop of a descendant that was painted before it.

Related limits this design accepts: the repaint is in the page's coordinate space, so a transformed element, or a transformed ancestor
between the element and its backdrop root, is not filtered/flattened; and content painted before the element by a *different stacking
context* than its ancestors' is included because the repaint follows the real paint order exactly.
