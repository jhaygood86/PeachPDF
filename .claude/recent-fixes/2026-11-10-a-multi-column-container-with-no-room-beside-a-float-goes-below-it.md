# A multi-column container with no room beside a float goes below it (part of #2318)

First document of #2318: an outer `columns: 2`, a `float: left` of 20 words that fills a column and continues into the next, then an inner `columns: 3` holding one word. The word was drawn at x = 180, the outer content edge, and clipped.

**Cause.** `CssLayoutEngineColumns.Layout` narrows a container to the room beside the floats that reach its top (`ExtentBesideFloats`), but only when 20pt or more is left; otherwise "it keeps the width it had". With the float filling the whole outer column there was no room, so the inner container was laid over the float, and the lines of its columns were pushed to the float's far edge (180), past every inner column.

**Change.** When a fresh layout (not a continuation) finds less than `MinimumRoomBesideAFloat` beside the floats and they end below the container's top, the container is moved below them (`Location.Y` = their lowest margin edge, `ExtentBesideFloatsWithBottom`'s `FloatsBottom`), then its width and extent are asked again from there. CSS 2.1 §9.5: a box that establishes a formatting context may not overlap a float; the UA may move it below. `PlaceBesideFloats` in the table engine does the same for tables, and is the pattern.

**Evidence.** Document 1 of #2318 draws its word once. Release engine suite 19,384 (3 skipped), `PeachPDF.Tests` 1,807, zero warnings. No existing row depended on the overlap.

**Not done.** The other two #2318 documents lose a different word: a paragraph after a float inside an inner container that is filled in two outer columns is laid out at the inner column-3 position of the first outer column (`loc=(110.7,76.9)` while the container is at x = 156), and no fragment claims it. Still skipped in `MulticolFloatIssueDocumentsTests`, with #2320 case 1.
