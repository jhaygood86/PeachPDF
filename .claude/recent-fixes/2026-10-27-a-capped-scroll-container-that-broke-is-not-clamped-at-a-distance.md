# A capped scroll container that broke is not clamped a cap's distance below its top (#1373)

Before: an `overflow: hidden` box with `max-height: 50pt` whose first lines fell near a page foot lost its last line.
In the issue's document the box starts 40pt above the foot and holds four 12pt lines (48pt). The fourth line
straddles the foot, and the widows rule (two lines on the next page) pulls the third over with it, so the box's content
is 24pt on one page and 24pt on the next. `ApplyHeight` still clamped the box's bottom at `Location.Y + max-height`
(190), a *document* distance that counts the 16pt strip the break skipped. On the second page the clip then ended at
y=30 while the pulled-over lines sat at 20-44, so `w4_186` was clipped away. The fragment tree held the word, so every
tree-level check passed; a PDF text extraction (PyMuPDF) did not find it.

Fix (`CssLayoutEngine.ApplyHeight`): for a capped scroll container (`CappedScrollContainer.IsCapped`) whose lines have
not used up the cap (`CapBottom` is null) and whose box spans more than one slot, no clamp is applied. The box ends where
its content does, which is below the cap's distance. A box that did not break keeps the old clamp, and one whose lines
do reach the cap keeps `CapBottom`.

What was found by running it: the tree-based oracle (`LayOutAndCollectVisibleAsync`, a word inside the band) is not
enough. The test (`ALineMovedPastTheFootOfACappedScrollContainer_IsNotClippedAway`) checks the word against its
fragment's `OverflowClip` as well, and against the content area's horizontal extent. It fails without the fix.

Related findings, not fixed here:
- #1369 does not reproduce. The document has no `@page`, and `new PdfGenerateConfig()` has `PageSize.Undefined` with a
  0x0 manual size, so the page has no content area and the PDF is blank. With `PageSize.Letter` nothing is lost.
- #1525's minimal reproduction needs a flex item in outer columns: the last 7 words land at x=-158 (two outer-column
  pitches left of the first column), and the flex item also leaves a stale copy of its text unbroken in the first column.
  Not at `AssignLocations` (dx is 0) and not through any `OffsetLeft`/`ShiftOwnLineGeometryLeft` over 100pt.
- #1485's heading line straddles the foot because the row is laid out without a fragmenting context; see the issue.

Evidence: Engine.Tests 16513 and PeachPDF.Tests 1499 on net8.0 (Debug), all passing.
