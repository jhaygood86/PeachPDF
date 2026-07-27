# `clone` decorations at a multicol boundary, outside the block-flow path

_Tracked by [#476](https://github.com/jhaygood86/PeachPDF/issues/476). Bounded by
[#335](https://github.com/jhaygood86/PeachPDF/issues/335)._

[§6.2](https://www.w3.org/TR/css-break-3/#break-decoration) is honoured for **block** content at a
column boundary — that is what the fix recorded in
[2026-07-27-a-continuation-column-reopens-its-block-start-decorations-only-under-clone.md](../recent-fixes/2026-07-27-a-continuation-column-reopens-its-block-start-decorations-only-under-clone.md)
did, at both layout sites. It is **not** honoured on the other three paths that meet the same
boundary. All three were measured, not inferred.

**1. Inline flow never re-opens a cloned box's own decorations at a column break.**
`CssLayoutEngine.CreateLineBoxes` computes the right amount and it does not reach the output: with
`box-decoration-break: clone` and `padding-top: 9pt; border-top: 5pt` on a paragraph split between
two columns, the continuation's first line is at **20.00 under both `slice` and `clone`**, where
`clone` wants 34.00 — and `DomUtils.ClonedBlockStart(p, null)` really does return 14.00 for that box,
with `HasCloneDecorations` true. So the arithmetic is written but inert here. **This is why the
inline path is the shape to copy and not a working precedent**; a change that "makes the block path
agree with the inline path" at a column boundary would be copying a no-op.

**2. `FragmentEmitter.BandCut` insets a fragment by the *container's* own cloned spacing.** Its
`DomUtils.ClonedBlockStart(box.ParentBox, stopAt: null)` walks past the multi-column container to the
document root. With the container cloning `padding-top: 9pt; border-top: 5pt` and a plain block
inside it split across the columns, the block's line fragment rects come out at **y = 34.00 while the
block's own `Location.Y` and its content are at 20.00** — its background and border paint 14pt below
the content they enclose, in both columns. The fix has a known shape: the same bound the layout side
now uses (`stopAt` the nested fragmentainer's context root) rather than the document root. It was left
out because it is the paint side, needs its own emitter tests and its own showcase evidence, and the
layout correction was already a full change.

**3. A container continuing onto a later page reserves nothing for what it re-opens there.** A
multi-column container with `clone` that spans a page boundary puts the first row of page 2 at
`PageTopOf(1)` exactly — delta **0.00** — so the border it re-opens paints over the text. This one
predates the block-path fix and is unchanged by it: the `Math.Max(ResumeContentTop, ClientTop)` that
fix replaced never covered the case either, because the container's `ClientTop` is its position on
the page it *began*.

**Do not "fix" 2 by widening the layout side to match it.** The layout side is right and the emitter
is wrong; making them agree in the other direction would re-introduce the 14pt indent the block-path
fix removed, and it would do so invisibly, because the two would then be consistent with each other
and wrong together.
