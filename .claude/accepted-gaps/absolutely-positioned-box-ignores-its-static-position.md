# An absolutely positioned box with `auto` offsets ignores its static position (#1303)

**Gap:** with `top`/`left` (or `right`/`bottom`) left `auto`, an absolutely positioned box sits at its
containing block's padding corner. CSS 2.1 §10.3.7/§10.6.4 place it at its static position, where it
would have been in flow. This holds in block flow and inline flow alike:
`aaa<span style="position:absolute">x</span>bbb` puts `x` at the containing block's corner, not after
`aaa`.

**Where:** `CssBox.CommitBlockChildOffset`'s absolute branch resolves offsets through
`ResolveOffsetOrZero`, so `auto` contributes 0. No static position is recorded anywhere.

One approximation leans on this: in a `middle`/`bottom`-aligned table cell,
`CssLayoutEngine.ApplyCellVerticalAlignment` moves a box whose offsets on the alignment axis are both
`auto` along with the aligned content, since its static position is in that content. That covers a
direct child of the cell and, through `MoveStaticallyPlacedDescendants`, one nested in an inline whose
containing block is outside the cell (the subtree translation skips such a box, `EscapesTranslationOf`).
Implementing the static position should make that move exact rather than remove it.

**Why out of scope:** it predates #1299 and is unchanged by it. #1299 made the inline case reachable
without a split, and the static position is now available cheaply there: `FlowBox`'s cursor when it sets
the box aside into `CssLineBoxCoordinates.AbsolutelyPositioned`. But the block-flow half needs its
own recording in `ResolveBlockChildOffset`, and both halves need `direction`-aware §10.3.7 logic.
That is a feature of its own, not part of the split fix.
