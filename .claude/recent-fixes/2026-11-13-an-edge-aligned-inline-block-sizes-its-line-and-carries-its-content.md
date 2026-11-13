# A top or bottom aligned inline-block with a declared height sizes its line, and its content moves with it

`<div style="display:inline-block; vertical-align:bottom; height:288px">text</div>` hung above its line: the declared height was added to the
open line's `MaxBottom` only, which the closed line's extent (`BaselineExtent`, what `ApplyVerticalAlignment` reads) never saw, and the box was grown
upward from the strut-sized line. Its text also sat at the box's bottom, because each descendant was bottom-aligned to the line on its own.

- `FinalizeFlowBoxExit` records the margin-box height in `TopAlignedAtomicHeight`/`BottomAlignedAtomicHeight` and commits the line extent, as an image
  aligned the same way already did (CSS 2.1 §10.8.1: the line is at least as tall as such a box). The extra goes above the baseline for `bottom`.
- `ApplyVerticalAlignment` moves every box inside an edge-aligned atomic box by that box's own delta (computed from the geometry before anything moved);
  `HeightenAtomicInlineRectangles` lifts the content with the top edge when it grows a bottom- or middle-anchored rectangle.
- Two tests encoded the old overflow (`bottom edge fixed`); they now assert the declared height and that the box reaches the line's top.
- Evidence: WPT +11 (floats-wrap-bfc-*, vertical-align-applies-to-012, margin-auto-on-block-box), none lost; `LayoutPaintCoverageTests` bottom/top cases.
