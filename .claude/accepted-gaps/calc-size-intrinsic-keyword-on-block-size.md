# `calc-size()` with an intrinsic keyword basis on `height`/`min-height`/`max-height`

`calc-size(auto | min-content | max-content | fit-content, <expr>)` works on `width`, `min-width` and `max-width`, and a
length or percentage basis works on every size. On the block axis an intrinsic keyword basis is rewritten to the
property's neutral value (`auto`/`0`/`none`), so `height: calc-size(auto, size + 20px)` is just the content height.

**Spec rule violated:** CSS Values 5 §10.8.1 - the basis is the box's intrinsic block size and the expression is applied to it.
Tracking issue: #2366.

**Why:** the block size does not exist until the content is laid out, and every reader of `Height` runs before that. The inline
axis is measured up front (`CssBox.ResolveCalcSizeWidthsAsync`, from `GetBoxWidth`, which has the canvas). Supporting the block
axis means re-running the height-dependent layout after the epilogue settles the content height, which touches every layout engine.
Not attempted here; the rewrite to the neutral value is deliberate so such a declaration degrades to `auto` rather than to a
garbage size.
