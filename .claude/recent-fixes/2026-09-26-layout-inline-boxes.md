# Inline boxes in `PeachDrawing.Text.Layout`

`ParagraphBuilder.AddInlineBox(InlineBox)`, `InlineBox`, `VerticalAlign`, `PlacedRun.InlineBox`, `PlacedRun.InlineBoxBounds`.

- **A box is a U+FFFC in the text, registered by offset.** The builder records `offset -> InlineBox` and appends the character through `AddText`, so the run bookkeeping (a box has the
  style of the run it is added in) is not duplicated; a U+FFFC the caller typed is not in the map and stays an ordinary (missing-glyph) character. `BuildAtoms` cuts a box into an
  atom of its own the way it does a tab, `Measure`/`TabWidths` give it its width, `Assemble` gives it a glyphless `Piece`, and `PlaceRun` gives it the tab's two caret boundaries.
  Fallback resolution skips it (a fallback face would only have changed the strut). UAX #14 already allows a break on both sides of U+FFFC (class CB) and UAX #9 treats it as neutral.
- **The line's height is a max over above/below the baseline, not a sum.** `BoxExtent` gives how far a text-aligned box reaches above and below the baseline (`Baseline`/`Middle`/
  `TextTop`/`TextBottom` plus the shift); `Top` and `Bottom` boxes only demand `Height` of the finished line and grow it on the side away from their edge. The text of every piece, boxes
  included, is still `Include`d, so a line of one box has its text's strut (a first version left an all-box line as tall as the box, which Chrome does not do in standards mode).
- The box's rectangle is worked out in `Build`'s placement pass, when the line's top, baseline and height are final, and reaches the caller as `InlineBoxBounds` (a `RectangleF`).
- Justification treats a box as a wall (like a tab and a generated run), so it is never stretched away from its neighbours.

Evidence: `ParagraphInlineBoxTests` (27: placement, breaking, every alignment, height with a line-height multiple, RTL order, carets and selection, ellipsis cut, validation, 5,000 boxes).
