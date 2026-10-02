# A balanced multi-column container deferred a table row page after page for ever (seed 235)

`LayoutRunSegment` fills a continuation at the full page budget, learns the real height of what remains, and, when the
content ends there, re-fills at an even share of it so the columns balance. If the even share cannot hold content that
does not divide (a table row whose cell holds a float: the row is taller than a third of its own height), every
trial carries something over, the trials are capped at four, and the loop ended with the unfinished last trial. The
full-budget fill that had finished the flow was thrown away and the container was deferred to the next page, which
did the same: the driver's no-progress check compares a token that names the slot, which differs every page, so it
never fired, and the render ran to the 100,000-pass cap (minutes of CPU, 100,000 pages). Found as a lost word in seed
235; the reduction is `columns:3` > table > a cell with a right float and one word.

Fix: remember that a fill at the full budget finished, and when the balance trials end carrying something over,
make that fill again. Only on a continuation (`resume` non-null): a first fill balances from an estimate, and
applying this to a nested container as well doubled words in seeds 170, 252 and 272, whose cause is that this exposes
what #1575 and #1577 fixed.

Not done: the same row in a container beside a left float (`x1`, an anonymous `<tr>` written without a table) still
loses its word at a different place; and the lost words of seed 235 itself are table cell text at the right page edge.

Evidence: corpus (300 documents) unchanged at 68 lost and 6 doubled; net8.0 suite green.