# A stale cursor can be load-bearing compensation for a bad estimate

_A trap this repo has paid for at least once. Discovered on [#432](https://github.com/jhaygood86/PeachPDF/issues/432),
which is now closed — this is the rule it left behind, not an open defect._

The table row loop's band used to be a counter, stale by construction — it named the band the loop last
*opened*, not the band the row cursor reached — and correcting it to read the cursor looked like a
one-line application of
[a resume target must come from where the break fell](fragmentation-a-resume-target-must-come-from-where-the-break-fell.md).
It was not. `EstimateRowHeight` under-reports a row's height by roughly 2× (one line of text, blind to
block content in a cell), and the stale band was the only thing that ever noticed: once the loop believed
it was on band `k` it kept measuring later rows against band `k`'s bottom, so an overflowing row was
caught one row late rather than never.

**Measured twice, three years apart in code terms and both times the same shape.** Applying the
derivation alone to `77e845d` — after [#488](../recent-fixes/2026-07-28-a-table-fills-one-fragmentainer-per-pass-and-is-resumed-in-the-next.md),
[#495](../recent-fixes/2026-07-28-a-repeated-header-sits-above-the-continuation-not-on-top-of-it.md) and
#508 — gave the three heights exactly right (row 1 at 723 / 1023 / 1423 for a 700 / 1000 / 1400pt first
row) and broke a repeating `<thead>`: header on **1** page where the fixture wants ≥ 3, **2** header
proxies where it wants ≥ 3, and `PageBreakBottoms` null on a table that had to break inside itself.

The general form, which is the reason this file outlives the issue: **before replacing a cursor that is
wrong, find out what its wrongness is currently covering for.** A predicted-fit check and a stale band
are two errors in opposite directions; removing one leaves the other unopposed. The order that worked was
the estimate first — the loop now places the row and asks
[`HtmlContainerInt.FallsPast`](../../src/PeachPDF/Html/Core/HtmlContainerInt.cs) of the bottom it really
reached, retracting and re-placing it where the answer says the break falls — and only then is deriving
the band from the coordinate safe, because the prediction is no longer the only question asked.

One measurement in the original note did **not** survive re-running: a 40-row table recording no break
anywhere. That test passes under the derivation alone on the current tree; its failure was arithmetic
coincidence rather than mechanism. `WholeTableRelocationTests.ATableThatBrokeBetweenItsOwnRows_IsNotMoved`
regresses instead, and was never named. Re-measure a claim like this before acting on it.
