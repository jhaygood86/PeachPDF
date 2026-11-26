# A table that broke inside a row is not moved whole

_Landed 2026-11-26._

**Issue #2493.** A table starting partway down a page and not fitting moved *entirely* to the next page (page 1 left blank) for some spacer heights and broke correctly for others. [css-break-3 §4.1/§4.3](https://www.w3.org/TR/css-break-3/#possible-breaks) and [css-tables-3 §6.1](https://www.w3.org/TR/css-tables-3/#fragmentation): a table with no `avoid` breaks where content runs out; only a box that did not fragment may be moved whole.

**Cause (found by tracing the three suspects, not by reading).** The §4.3 mover in `CssBox.PerformLayoutEpilogue` asks `PaginatedItsOwnContentWithoutBreaking`, which read "no `PageBreakBottoms` entry" as "the table did not fragment". `PageBreakBottoms` is written only for a break taken *between* rows. When a cell ran out of fragmentainer inside a row (the row fragments, §6.1) the engine published a `TableContinuation` and the resumed pass placed the rest, but wrote no entry — deliberately, since that dictionary clips the table's borders — so the mover moved the whole, already-fragmented table to the next page. Which spacer heights failed depended only on whether the straddling row's cell stopped (fragmented) or the row was moved whole by the straddle correction (which does record an entry).

**Fix.** `CssBox.TableBrokeInsideARow` (rare state) is set by `CssLayoutEngineTable` when a pass stops inside a body row *after the first one* and cleared on a fresh (non-continuation) run; `PaginatedItsOwnContentWithoutBreaking` reads it. **Trap:** a stop inside the *first* body row must not set it — nothing of the table stays above it on that page, so the table did not fragment and moving it whole is still right (`TableRelocationHeightTests` caught this).

**Not done.** A cell that stops leaves rows split mid-text (e.g. one line of a `<br>`-separated cell before the break), which §6.1 permits but does not honor orphans/widows inside the cell; left as is.

Tests: `TableStraddlingBreaksBetweenRowsTests` (the issue's document at six spacer heights; fails without the fix at 500/550/700/750 by leaving page 1 holding only the spacer).
