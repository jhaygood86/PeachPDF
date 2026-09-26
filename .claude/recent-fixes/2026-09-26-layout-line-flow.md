# The host-driven line tier: `Paragraph.CreateFlow`, `LineFlow`, `FlowCursor`, `LineSpace`

`LineFlow.TryNext(in FlowCursor, in LineSpace, out LineBox, out FlowCursor)` lays out one line and never mutates anything, which is the point: PeachPDF's inline layout undoes lines by
snapshot and restore, and this tier turns "undo" into "call again with an earlier cursor". PeachPDF does not switch to it (see the accepted gap).

- **The purity was built in earlier and is only exposed here.** The text-indent PR made line breaking a function of `(paragraph, start, room, pen)`; hyphenation added the count of hyphenated
  lines in a row as an argument (`FlowCursor` carries it, next to the offset and the end flag, and is why it is a struct rather than an `int`); `Build` then had two phases (measure, place) and
  this PR split them into `MeasureLine` and `PlaceLine` so that `Build` and `LayFlowLine` are the same code with different areas. `Build` is unchanged in behaviour except for the alignment rule below, which the test that
  runs 19 styles through both (plain, indents, tabs, justification, hyphenation, boxes, ellipsis, RTL, ...) at three widths pins down.
- **`default(FlowCursor)` is the start**, so a caller needs no factory, and a cursor cannot be forged: there is no constructor. The only check on a cursor is that its offset is inside the text.
- **What a space can be:** `Left` finite, `Right` any number (infinity means no wrap; less than `Left` is no width), `Indent` and `Top` finite. A hostile space throws `ArgumentException`; a
  narrow one still consumes a cluster per line, so the loop always ends (tested with zero, negative and minus-infinite widths, tabs, boxes and soft hyphens).
- **Alignment gained two rules for this tier** that `Layout` shares now: a line wider than its area is start-aligned instead of being centred or right-aligned into negative space (CSS Text 3
  `text-align`), and in an area with no end (`Right` infinite) every line is start-aligned since there is no far edge.
- **Not applied by the flow:** `MaxLines` (the caller counts lines). The last-full-line hyphenation limit guesses the next line's room from this space's width less an unindented line's indent (a review measured 6 of 342 width/indent combinations laying out differently from `Layout` when it used this line's room, and the theory had no case with the limit). Tab stops start
  at the space's edge; a caller with a float that narrows the space cannot get CSS's block-edge stops from this API.

Evidence: `LineFlowTests` (43 including the equivalence theory), every other layout test unchanged.

A review also found that a `Right` far enough below `Left` for `Right - Left` to overflow to minus infinity read as "no end" (the whole paragraph on one line) instead of no width, and that `Left + Indent` could overflow to infinity in the output: the width is clamped at zero and the sum is validated.
