# `text-align` ignores the width of an `inline-flex` / `inline-grid` / `inline-table` box on its line

[CSS Text 3 §7.1](https://www.w3.org/TR/css-text-3/#text-align-property): an atomic inline-level box's width
counts towards the line's content width when the line is aligned.

PeachPDF registers these boxes on the line through `Line.Rectangles` rather than as words, so the alignment
offset is computed as if the box were absent. Probed in a 300pt `text-align: center|right` block: the box
alone on its line is not moved at all (X stays 20), and the box after the text `ab ` is shifted by the offset
a line of only that text would get, so a 120pt box lands at X≈179 (center) / ≈323 (right, ending past the
container's right edge). An `inline-block` with a declared width and text looked similar in the same probe but
was not investigated.

Under `text-align: center|right` or `dir=rtl` the box's background does move (x 240 against Chrome's 231.5) while
its in-flow item text stays at x=32.5, and an absolute child stays with the items about 200px from the painted box.

Found while testing out-of-flow children of `inline-flex`
([recent fix](../recent-fixes/2026-10-09-inline-flex-out-of-flow-children.md)), whose tests therefore pin only
the child's offset from its container under `text-align`, not the container's position (a box alone on its line is
not moved at all, so a `Location` check would pass for the wrong reason).

Tracked as [issue #1679](https://github.com/jhaygood86/PeachPDF/issues/1679).
