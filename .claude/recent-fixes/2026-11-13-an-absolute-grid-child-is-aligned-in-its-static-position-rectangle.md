# An absolutely positioned grid child ignored `align-self`/`justify-self` at its static position

css-grid-1 section 9: with `auto` insets the child sits as the only item in a grid area whose edges are the grid container's content edges (or the area its lines name when the grid is its containing block), and is aligned there by `align-self`/`justify-self`, the container's `align-items`/`justify-items` where those are `auto`. The engine put it at the area's start corner whatever the alignment.

`CssBox.AlignStaticPositionInGrid` runs in the absolute-placement epilogue (the box has its size by then): on each axis whose two insets are `auto` it offsets the box by the free space times the alignment's factor (`center` .5, `end`/`self-end`/`flex-end`/`last baseline` 1, `safe` falling back to start where it overflows). Horizontal, left-to-right grids only; `CssLayoutEngineGrid.ResolveSelfAlignment` became internal for it.

css-grid +11 (`grid-abspos-staticpos-align-self-*`, `-align-items-*`, `-justify-self-*-001`); `LayoutPaintCoverageTests`.

## Not done
The `-rtl`, `-vertWM` and `-002` variants, and the `-large-border-padding` ones (the grid has a border and padding the content box must exclude), were not looked at.
