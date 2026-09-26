# A translation moves a line's FlowTop with its words

`CssLineBox.FlowTop` (and `BaselineY`) is a number a closed line records about where the flow placed it. It
is not a view onto the line's words. Two things read it:
- `CssLineBox.LineTop`;
- the fragment emitter, directly, to decide which fragmentainer a line belongs to when the line's ink rises
  above it (`FragmentEmitter.InkRiseAboveLineTop`).

So every translation of a line's words has to move it too. A line is held in its **owner's** `LineBoxes`
(the block its words flow in), not in the inline boxes that own the words.
- `CssBox.OffsetTop` moves the lines of every box it moves (`CssBox.OffsetOwnLineBoxesTop`). That covers a
  mover that moves the owner.
- A mover that moves the owner's children but not the owner has to call `OffsetOwnLineBoxesTop` on the
  owner itself. A table cell's vertical alignment (`CssLayoutEngine.OffsetCellContent`) is one: a cell with
  its text directly in it owns that text's line, and the alignment moves only the cell's children.
- A mover that shifts words or rectangles by hand has to do the same.

**What breaks otherwise.** A line whose tall glyph moves it to a page's top is claimed by the page it left,
and is drawn on no page. That was measured as a card's heading lost at a page foot in the #1334 review.

See [the fix](../recent-fixes/2026-09-26-a-line-is-claimed-by-the-page-its-line-box-is-on.md).
