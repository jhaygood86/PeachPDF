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
make that fill again. Only on a continuation (`resume` non-null): applying it to every fill doubled words in
seeds 170, 252 and 272 (nested containers). Seed 252's were the spilled word #1575 fixes, which this ordering exposes
again on a build without it; 170 and 272 were not reduced, and stay out by the `resume` condition.

The same change moves content: a continuation whose flow fits the page in an unbalanced fill is no longer deferred to
the next page because the balanced trials did not fit. `NestedMulticolContentTests` needed 50pt more content above its
columns to still run onto a second page.

Not done: seed 235's own lost words (`w235_42`-`52`, absent from the fragment tree altogether) are not fixed by this.
The reducer's smallest document for them is a bare `<tr>` with a right float and a `<td>` in a `columns:3` container
(`<div style='columns:3'><tr><div style='float:right;width:72pt;height:69pt'></div><td>word </td></tr></div>`), which
loses its word on main with and without this change.

Evidence: corpus (300 documents) unchanged at 68 lost and 6 doubled; net8.0 suite green.