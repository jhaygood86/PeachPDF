# An inline-block or inline-table crossing the page foot moves to the next page whole (css-break-3 §4.1, §4.4)

The release-notes session found #1487's document losing two words that v0.9.20 drew (`w384_391`, `w384_395`: the last lines of the cells of a table
in an `inline-block`, 1pt above the foot of the page); bisect put it at #1520, and the same document recovers eleven other words.
The cause is older than #1520. The content of an inline-block is laid out whole, with page breaks suppressed (`SuppressWordPageBreaks`, no
fragmentainer), and the emitter slices it where it crosses the foot: a line across the foot is claimed by the page its top is on and clipped
there, so its words are in the tree and in no visible pixel. Whether that line is the last of the box (the box ends 1pt below the
foot) or a middle one, the plain-text case `display:inline-block;width:60pt` with 24 words does the same at every commit measured, before and
after #1520 (built and compared with the CLI). #1520 only moved what else is on the pages, which decides whether the clipped line is
hidden by a later pass.

css-break-3 §4.1: "inline-block and inline-table boxes (and other inline-level display types that establish an independent formatting
context) may also be considered monolithic", and §4.4 breaks before such a box when it fits a fresh fragmentainer instead of slicing it
(slicing is for content that cannot fit anywhere: "avoid losing content off the edge"). The engine already says as much where it
dispatches an inline-block ("treated as monolithic within the surrounding inline flow") but only asked a word's question, never the box's.

Fix: after an inline-block or inline-table is placed, `BreaksBeforeAnAtomicInlineThatStraddles` asks whether it crosses the foot of the page
being filled; if it does and its depth from the line's top would fit a fresh page, the flow takes a break at the start of its line (as for
a word that straddles: a line box is unbreakable), and the box's own words are marked as awaiting the next fragmentainer so the page
being left does not keep claiming them. A box too deep for any page is still sliced. Page-level flow only; not for inline-grid
(`AnInlineGridContainer_IsLeftToItsLine` records an earlier decision about it) or inline-flex, and not inside columns.

Evidence: the issue's document goes from four lost words (`391`, `395`, `1119`, `1131`) to one (`1141`, not examined); net8.0 suite
green; the new test fails on main.

Not done: an inline-block taller than a whole page is still sliced, and the line across the foot is clipped on the page that claims it (the
spec's slicing would draw the part of it in each page); `w384_1141` and `w384_1164` of the same document are other losses.