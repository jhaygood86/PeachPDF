# A widened table's surplus is proportional in both clauses, not just one

`DetermineMissingColumnWidths` has two surplus clauses. The one for a table whose columns all
declared a width already called `SpreadSurplusProportionally`. The one for a table whose columns are
all `auto` divided the surplus by the column count and added the same absolute amount to each —
so the two clauses disagreed about what CSS 2.1 §17.5.2.2's "distributed over the columns" means,
and the auto case is by far the more common one.

## What measuring it turned up

- **Chrome settles it.** On a three-column fixture at 500pt — a long description column plus `Qty`
  and `N` — Chrome 152 gives 451.2 / 32.9 / 15.9. Proportional gives 447.3 / 36.1 / 16.7, within
  ~4pt. An equal share gives 299.7 / 104.7 / 95.5: the description column loses 150pt to two columns
  holding three characters between them.
- **`main` was not doing a pure equal share either**, which is worth knowing before reading the old
  code as simpler than it is. Measured bumps were 112.5 / 75.2 / 75.2 — the two small columns took
  identical absolute shares while the large one took more, because the max-content capping loop
  above runs first and removes some columns from the split. The result still sits much closer to
  equal than to Chrome.
- **The natural widths are not the internal state.** The first fixture computed each column's
  expected share from a separate `width: auto` render and was out by 7pt, because that capping loop
  has already moved `_columnWidths` by the time the surplus clause runs. Comparing each column's
  SHARE of the finished table — against Chrome's shares — is both simpler and the thing the rule
  actually states.
- **The `max-width` cap had to survive.** `GetColumnExplicitMaxWidths` still bounds each column;
  only the size of the share changed. A column that cannot absorb its share leaves the table
  slightly under-filled, which is the pre-existing behaviour and deliberately not changed here.

## Evidence

`TableSurplusDistributionTests`, four fixtures: shares matching Chrome's 90.2/6.6/3.2, each column's
bump ordered by its own size, the wide column still holding ~90% of the table, and the `max-width`
cap. Three fail against `main`. Full suite green on net8.0 (10,256), 0 new build warnings.
