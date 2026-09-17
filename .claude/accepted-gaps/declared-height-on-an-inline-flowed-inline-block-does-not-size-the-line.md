# A declared height on an inline-flowed inline-block does not size the line

Tracked as **#1166**. Left in place by #1101, which made a declared `height`/`min-height` size the
box's own painted background/border/overflow-clip; the line-sizing facet of the same underlying
approximation (an inline-block whose own content fits on one line is flowed into the surrounding
line boxes rather than laid out as a genuine atomic unit) predates it and was already documented in
`docs/html-css-support.md` before #1101 landed.

CSS 2.1 [§10.8.1](https://www.w3.org/TR/CSS21/visudet.html#line-height) has an atomic inline-level
box contribute its whole margin box to the line's height. Today the line still sizes itself from the
flowed content's own font metrics plus the box's padding/border, so
`<span style="display: inline-block; height: 100px">x</span>` reserves only its one line of text's
natural height on the line, not 100px — even though the box itself now correctly paints at 100px
tall (#1101).

## Why it stays out

`CssLayoutEngine.FinalizeFlowBoxExit` already has a height-extension branch for the line
(`coordinates.MaxBottom - trueStartY < box.ActualHeight`), but nothing upstream of it ever resolves
a declared `height`/`min-height` into `box.Size`/`box.ActualHeight` for this box shape — that
resolution only happens via `CssLayoutEngine.ApplyHeight`, reachable solely through ordinary
block-child layout, which this box never goes through
(see [`ResolveAtomicInlineDeclaredHeight`](../../src/PeachPDF/Html/Core/Dom/CssLayoutEngine.cs)'s own
remarks). #1101 deliberately does not resolve `Size.Height` at all, correcting only the box's own
per-line painted rectangle after the line's height has already been decided — an early attempt at
also growing the line (by running the correction before `ApplyVerticalAlignment`) fed the grown
rectangle into that method's baseline-extent folding (CSS 2.1 §10.8.1's "atomic inline contributes
its whole margin box" logic, which already exists for replaced content) and genuinely grew the line,
but left the *containing block's* own height uncorrected (computed earlier, from the flow's original
`MaxBottom`) — pushing the block's next sibling to overlap the now-taller line instead of clearing it.
Closing this needs its own design pass on that interaction, not a one-line reordering.

Only the inlines-only case is affected: an inline-block holding block-level content, or one whose own
inline content must wrap, goes through `FlowAtomicBlockContentChild`, which already gets height right
because it is genuine block layout.
