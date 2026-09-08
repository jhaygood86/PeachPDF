# A widened table's extra space is shared in proportion to the columns, not equally

A table with a declared `width` whose columns are all `auto` has surplus space to distribute once
each column has been resolved to its own max-content width. That surplus was handed out as an equal
number of **points per column** — the same absolute amount to a 250pt description column and a 30pt
quantity column — so widening a table to `width: 100%` did not produce the same table, only wider.

The surplus is now shared **in proportion to what each column already measures**, which is what
browsers do and what the neighbouring all-columns-specified clause already did.

**What a document author may notice.** Column boundaries move in any auto-layout table that declares
a width wider than its content — generally toward what a browser produces. On a three-column
fixture at 500pt (a long description column plus `Qty` and `N`):

| | description | Qty | N |
| --- | --- | --- | --- |
| Chrome 152 | 451.2 | 32.9 | 15.9 |
| after this change | ~90% of the table | | |
| before (equal share) | 299.7 | 104.7 | 95.5 |

An explicit `max-width` still caps a column, exactly as before.

See [Tables](../../docs/html-css-support.md#tables) in `docs/html-css-support.md`.
