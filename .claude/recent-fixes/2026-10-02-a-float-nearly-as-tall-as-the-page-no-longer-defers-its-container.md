# A float nearly as tall as the page, in nested columns, deferred its container for ever (seed 156 with tall floats)

`ShiftEmptyLineBelowCrowdingFloats` moves a line that has no room beside a float down below it (CSS 2.1 §9.5), stopping when
the shifted line would start at the foot of the fragmentainer. A float 208pt tall on a 210pt page does not trip that: the
line is shifted to y=228, which is above the foot, and then it does not fit there (it ends at 240), so the container breaks
before it. The float belongs to that line, so it goes to the next page with it (#1564), and there the line is again at the top,
shifts below the float again, and again does not fit. The driver's no-progress check compares a record that names the slot, which
differs every page, so it never fired: a render that ran to the 100,000-pass cap. A float of 150pt, or one taller than the
page (which has its own fragmentation), finished.

Found by running the corpus with floats up to 520pt tall (`CORPUS_TALL`): seed 156 hung. Reduction: `columns:3` > `columns:3` >
a left float 60 to 127pt wide and 208pt tall, then some words.

Fix: a line that is at the top of its page and would still not fit on a fresh page once moved below the floats stays where it
is, beside them (`CannotFitBelowTheFloatsEvenOnAFreshPage`). Where a fresh page would have room, it still goes there.

Evidence: tall-float corpus (300 documents) completes, 14 lost and 7 doubled words across 8 documents, where it hung at seed 156
before; the ordinary corpus is unchanged; the new test fails on main (it does not return).

Not done, and what the tall-float corpus shows is left (all float-fragmentation cases, see #1523 and the accepted gap on a float
that does not fit in its column): seed 212 (9 words) puts the line after a 316pt float in a 22pt column beside two taller
left floats at x=343, past the page edge; seeds 39, 204, 30 and 147 draw a word twice from a float wider than its inner
column laid out in two fills (`d1b`/`d2` of seed 112); seed 17 loses one word.