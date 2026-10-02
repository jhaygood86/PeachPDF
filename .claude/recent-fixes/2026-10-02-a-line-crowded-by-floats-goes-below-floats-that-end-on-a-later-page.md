# A line with no room beside floats that end on a later page goes below them (CSS 2.1 §9.5)

CSS 2.1 §9.5: "If a shortened line box is too small to contain any content, then the line box is shifted downward (and its width
recomputed) until either some content fits or there are no more floats present." `ShiftEmptyLineBelowCrowdingFloats` did that, but
refused the shift when its target was below the foot of the fragmentainer being filled, or when a line at the top of the page would
not fit on a fresh page after it (#1588): the line then stayed where it was, beside the float, in room it did not have. With a
float as wide as the page, or wider than its block, and taller than the page, that put the text at the right of the float, past
the margin, and drew the words partly or wholly off the page. The refusals were written for floats that move with the line (one
anchored in it goes to the next page when the line does, and the question is asked again unchanged: the hang fixed in #1588); for a
float placed before the line they have no reason: it ends where it ends, every page asked leaves less of it, the line goes to the
fragmentainer its end falls in (css-break-3 §4.4: content is not lost off the edge), and the flow breaks before it in the usual way.

Fix: the shift is taken, and the refusals skipped, when no float that crowds the line was placed on the line itself, and the line is in
the page-level flow, and no such float is inside a multi-column container.

Why the last two limits, measured on the corpora (ordinary and with floats up to 520pt tall): without them seeds 52, 100, 101, 112 and 196
drew words twice. A float in a column that continues into the next column has one geometry for both parts, and the columns of
a container share one band, so the continuation cannot be told apart from the first part; and a line deferred out of a column fill takes the
container's carry with it. That is the float-fragmentation core #1523 describes, not a placement rule. Seed 212 of the tall corpus
(9 words lost: the line after a 316pt float inside a 22pt column, beside two page-level floats) is that case and is not fixed here.

`CrowdedLine_BelowFloatsTallerThanThePage_StaysWhereItWas` asserted the old behaviour on its premise ("the next page would be asked the same
question"), which holds only for a float on the line; it now asserts the line is below the floats.

Evidence: ordinary and tall-float corpora unchanged from the same base (3 lost / 0 doubled; 15 lost / 7 doubled); the two cases that fail on
main (a page-wide float and a float wider than its block, both 400pt tall) pass; net8.0 suite green on Windows and Linux (Release).