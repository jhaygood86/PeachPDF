# A text decoration's ellipsis cut ignores boxes that never paint (#1631)

`EllipsisCutOf` re-derives the cut the word painter will make by replaying `PlanLineTruncation` over every
fragment on the line in tree order, first cut wins. The word painter makes the same decision through
`_linesAlreadyTruncated`, but only over fragments `PaintFragment` actually enters, and it skips a fragment whose
line rectangles are all outside the current clip (`IsAnyRectVisible`).

In an RTL block a Latin run overflows toward the end edge, so its first box in tree order (the plain `x `)
sits beyond the clip. The painter never enters it, but the decoration's replay did, found it "cutting" with an
anchor off the box, and clamped the span's underline there: the underline ran past the ellipsis out to the
edge. Measured on the issue's repro: underline x = 17..175, ellipsis ending at 38; now 38..175.

The fix skips fragments the painter would skip, using the clip in force when the decoration is painted (all boxes
on one line share the containing block's overflow clip). The issue suggested ordering boxes by visual position; that
was not needed for this case and would have made the decoration disagree with the word painter, which also
decides in tree order. If a plain box is only partly visible it does claim the cut in both, so the two stay in
agreement. Test: `TextOverflowDecorationTests.Rtl_PlainBoxBeyondTheClipBeforeADecoratedBox_...`, which fails
without the fix.
