# A float of another column does not start a line of this one at the float's far edge (part of #2317)

Found while digging into #2317's document with a sweep of generated variants (4 leading paragraphs x right float x left float sizes, `columns: 2`, 300x250pt pages; 128 documents, 46 lost words before, 41 after). The simplest losing document was `<p>15 words</p><div style="float:left">40 words</div><p>1 word</p>`: the last word was drawn at x = 280 on page 3, clipped.

**Cause.** The float is a continuation spanning two pages, and its box is one rectangle (column 2, y 230 to 445.9 in document space). The paragraph after it starts page 3 in column 1 at y = 440, which is above the float's bottom, so the left-float lookup found it by Y alone (`SeekPastGap` accepts a float that starts right of the cursor on the same line) and `FloatLeftEdge` moved the cursor to the float's right edge, 280. `ShiftEmptyLineBelowCrowdingFloats` then derived a 260pt `inset` from the arrived position and started the line at baseX + 260.

**Change.** `LeftFloatAt` returns nothing for a float that lies wholly outside the column of the line being laid out (its margin box ends at or before the line's content left, or starts at or after its content right) when the line is in a column fragmentainer. css-multicol-1 §2: a float belongs to the column box it appears in. The shift already filtered this way (`InThisColumn`); the word-level lookup did not.

**#2317's own document** was a different mechanism, fixed separately: see [the float after a resumed float](2026-11-10-a-float-after-a-resumed-float-keeps-the-column-top-stated-for-it.md).

**Evidence.** Engine suite 19,397 (Debug) with the 2317 row skipped; the new `ALineInAnotherColumnIsNotStartedAtAFloatOfTheNextColumn` fails without the change; zero warnings.
