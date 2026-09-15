# A percentage `width` on an inlines-only inline-block is ignored

Tracked as **#1097**. Left in place by #1091, which made an *absolute* declared width size the box;
the exclusion itself predates it (#951 introduced it, with a fixture asserting it).

`CssLayoutEngine.ResolveAtomicInlineDeclaredWidth` declines anything ending in `%`, so such a box
shrink-to-fits — [CSS 2.1 §10.3.9](https://www.w3.org/TR/CSS22/visudet.html#inlineblock-width) sends
a non-replaced inline-block to shrink-to-fit only when `width` is `auto`, and a percentage is not
`auto`.

## Why it stays out

The width is resolved where the surrounding line *places* the box — inside `FlowBox`'s per-child
loop, which holds the block being flowed and a cursor, not the child's own containing block. A
percentage resolves against that containing block's width ([§10.2](https://www.w3.org/TR/CSS22/visudet.html#the-width-property)),
so closing this means either plumbing the containing block to the placement point or moving used-width
resolution somewhere that already has one. Both are larger than the change that made an absolute
width work at all.

Only the inlines-only case is affected: an inline-block holding block-level content goes through
`FlowAtomicBlockContentChild`, whose `GetBoxWidth` call does resolve a percentage.

## What pins it

`InlineBlockDeclaredWidthTests.APercentageWidthIsLeftAlone` (the advance) and
`InlineBlockDeclaredWidthGeometryTests.APercentageWidthLeavesThePaintedBoxToTheContent` (the painted
box) both assert the box behaves exactly like an `auto`-width one. Closing the gap means rewriting
those two fixtures, which is the point — the exclusion reads as a decision rather than being
rediscovered.

Documented for readers, without the issue number, under
[Atomic inline-level layout](../../docs/html-css-support.md) in `docs/html-css-support.md`.
