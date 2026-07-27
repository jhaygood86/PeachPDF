# A stale cursor can be load-bearing compensation for a bad estimate

_A trap this repo has paid for at least once. Tracker: [#432](https://github.com/jhaygood86/PeachPDF/issues/432)._

The table row loop's band counter is stale by construction — it names the band the loop last *opened*, not
the band the row cursor reached — and correcting it to read the cursor looks like a one-line application of
[a resume target must come from where the break fell](fragmentation-a-resume-target-must-come-from-where-the-break-fell.md).
It is not. `EstimateRowHeight` under-reports a row's height by roughly 2× (one line of text, blind to block
content in a cell), and the stale band is the only thing that ever notices: once the loop believes it is on
band `k` it keeps measuring later rows against band `k`'s bottom, so an overflowing row is caught one row
late. Re-derive the band per row and every row is comfortably inside a *fresh* band, so no break is ever
taken — measured as a 40-row 1400pt table on 842pt pages recording zero breaks and a repeating `<thead>`
appearing on one page instead of five.

The general form: **before replacing a cursor that is wrong, find out what its wrongness is currently
covering for.** A predicted-fit check and a stale band are two errors in opposite directions; removing one
leaves the other unopposed.
