# Centring a cell in a row taller than its band must not carry a line over the foot (#1485)

Before: in a table whose row (129.6pt) was taller than the band (120pt), the first cell was `vertical-align: middle`
(the default) and was centred in the whole row. The row is laid out as one strip, so the centring offset (4.5pt) moved
the heading's second line from 126-138 to 130.9-142.4 against a foot at 140: the line was cut by the page and its word
was drawn on none. The line was checked against the foot while it was laid out (it fitted, bottom 619.88 against 620);
the alignment came afterwards and nothing asked again. `LastResortRelayouts` was 0, so this was not the last-resort pass
a code reading suggested.

Fix (`CssLayoutEngine.ApplyCellVerticalAlignment`): the offset is capped by the room above the nearest foot of every line
that ends inside its band (`RoomAboveTheNextFoot`). The content sits slightly higher than a true centre in a row that
crosses a foot, which is what keeps it whole; the earlier `AlignmentBottomFor` already does the same for content that
fits the band.

Found by running it: the delta-reduced document (five earlier blocks are all needed, the loss depends on sub-point
offsets) and `OffsetTop` traced with its caller, which named `ApplyCellVerticalAlignment`. The straddle check in
`FlowBox` was never the problem.

Not done: a cell centred across more than one foot is capped by the nearest one, not nudged by a line height per
boundary as Chrome does (it pushes the straddling line to the next page).

Evidence: `AMiddleAlignedCellInARowTallerThanItsBand_KeepsItsLastHeadingLineOffTheFoot` fails without the fix; Engine.Tests
16516 and PeachPDF.Tests 1499 on net8.0 (Debug).
