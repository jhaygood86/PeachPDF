# Inline-block mixed content is normalized inside its own formatting context

The `cascade_layers` showcase exposed two stacked failures. Every card is an `inline-block` whose
first child is a block-level `<b>` and whose remaining description is a bare text run. The title
painted, but the description did not; after that was corrected, the already-recorded #1105 width/wrap
gap would have made correctly-sized cards overhang the page.

## Why only the bold title painted

`CorrectInlineBoxesParent` and `CorrectBlockInsideInline` correctly treated an atomic inline as opaque
to its *parent's* inline formatting context, but their traversal stopped there altogether. The
inline-block's own independent block-container formatting context was never normalized. Its `<b>`
therefore reached block-child layout normally, while the following bare inline text never received
CSS 2.1 §9.2.1.1's anonymous block wrapper. Block-child layout invoked that text box in isolation: it
ended with zero width and no line boxes, so painting had no glyph fragment to draw.

The normalization walks now cross an otherwise all-inline ancestor chain specifically to find an
`inline-block`, then process that box as a new formatting-context root. Other atomic displays remain
opaque: flex/grid item generation and anonymous-table generation own their interiors, and treating
them like block containers regressed an inline-flex item containing a block.

## The coupled width and wrap correction

`FlowAtomicBlockContentChild` used to interpret a declared content-box `width` as the whole border
box for block-content inline-blocks. Correcting the cards from 150px to the browser-correct 182px
(150px plus 16px padding on both sides) could not land alone: without atomic wrapping, card four ran
off the page.

The method is now split so the inline-block's used width is available before its contents are laid
out. That preflight compares the whole margin box with the active line/floats; if it does not fit and
the line already has content, it applies `line-clamp` if needed and otherwise opens a new line through
the same `OpenNextLine` bookkeeping ordinary word wrapping uses. The content is then laid out once at
its final position. The atomic box's bottom margin edge also extends the parent line before that next
line is opened; previously the second card row touched the tallest card above it instead of preserving
the showcase's `margin-bottom: 12px`. Five showcase cards now form the expected three-plus-two rows
with the browser-equivalent inter-row gap.

## Evidence

- Regression tests assert the description receives an anonymous block wrapper, lays out below the
  title, and reaches the fragment painter as a real text draw.
- A fixed-width three-card fixture asserts content-box sizing (80pt + 20pt padding), two cards on the
  first 220pt line, the third whole card at the first line's x-position on line two, and the previous
  row's 12pt bottom margin preserved between them.
- A `line-clamp: 1` fixture asserts an atomic wrap is stopped before the hidden card is laid out or
  painted.
- The real `cascade_layers` source was rendered and rasterized: every description paints and the five
  correctly-sized cards occupy three cards on row one and two on row two.
