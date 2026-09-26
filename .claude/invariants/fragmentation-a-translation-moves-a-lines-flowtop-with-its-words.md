# A translation moves a line's FlowTop with its words, and a plain inline box's Location is not geometry

`CssLineBox.FlowTop` (and `BaselineY`) is a number a closed line records about where the flow placed it,
not a view onto its words. `CssLineBox.LineTop` reads it, and the fragment emitter reads `FlowTop` directly
to decide which fragmentainer a line belongs to when the line's ink rises above it
(`FragmentEmitter.InkRiseAboveLineTop`). So every translation of a line's words has to move it too. A line
is held in its **owner's** `LineBoxes`, the block its words flow in, not in the inline boxes that own the
words. `CssBox.OffsetTop` moves the lines of every box it moves (`CssBox.OffsetOwnLineBoxesTop`), which
covers a mover that moves the owner. A mover that moves the owner's children but not the owner has to call
`OffsetOwnLineBoxesTop` on the owner itself. A table cell's vertical alignment
(`CssLayoutEngine.OffsetCellContent`) is one: a cell with its text directly in it owns that text's line,
and the alignment moves only the cell's children. The same applies to a mover that shifts words or
rectangles by hand. Otherwise a line moved to a page's top with a tall glyph is claimed by the page it left and
drawn on no page. That was measured as the heading lost from an `overflow: hidden` card at a page foot in
the #1334 review.

The converse trap: a plain (non-atomic, in-flow) inline box's own `Location` is **not** placed by the flow.
Its geometry is its rectangles and words. `OffsetTop` still translates that `Location`, so the flow resets
it wherever it resets the rectangles (`ResumeOrdinal == 0`). Without the reset, a float moved whole onto
the next page carried its inline boxes' `Location` another move further on every layout of the same tree,
which `PdfGenerator`'s measure-then-final passes and a `target-counter` document's re-layouts both do. Don't
read an inline box's `Location` as a position. Read its rectangles.

See [the fix](../recent-fixes/2026-09-26-a-line-is-placed-by-its-line-top-and-a-moved-float-relays-out-the-same.md).
