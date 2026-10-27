# A flex item in columns was moved off the page when its pass was laid out twice (#1525)

Before: a `display: flex` item whose text runs over several columns and pages lost its last lines (7 words in the
smallest case) and, with nested `columns`, a column's worth of text. The words were laid out at x=-158, two outer-column
pitches left of the first column (x=20 minus 178.7), so they were off the page; the fragment tree held them.

Cause: `ResumeCommitPass` moves the items the break token names by how far the container moved since the token's
`PlacementOrigin`. The last pass was resumed twice from the *same* token (the log showed two consecutive passes with
origin (198.7, 500) at the container's (20, 660)); the first run moved the item to (20, 660), the second found it
unfinished again and moved it by the same delta again, to (-158.7, 820). Traced with a setter hook on `Location`, after
`OffsetLeft`/`ShiftOwnLineGeometryLeft` and `AssignLocations` had been ruled out (they never moved it).

Fix: `ItemContentCommit.ResumeDelta` keeps, per token (`ConditionalWeakTable`, by identity, because a field on the
record would join its equality), where the items currently sit, and returns the move from there. A second run from
the same token with the container where it was gives a zero delta. Flex row, flex column and grid use it.

Not done: with the inner `columns` the second page's text starts in the second outer column (x=109) instead of the
first, so the first outer column is left empty on that page; nothing is lost. The flex item also keeps a stale unbroken
copy of its text below the page (130 words down to y=1958), clipped away, which did not reach the PDF text.

Evidence: `AFlexItemInColumns_IsNotMovedTwiceWhenItsPassIsLaidOutAgain` (both variants) fails without the fix; the issue's
document has 0 lost and 0 doubled words in the PDF text (was 38 lost); Engine.Tests 16516 and PeachPDF.Tests 1499.
