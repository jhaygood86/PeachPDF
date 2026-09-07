# Named-page reversion now honored inside tables (#166, table case)

`CssLayoutEngineFlex`/`CssLayoutEngineTable`/`CssLayoutEngineColumns` position their children directly,
bypassing the block-flow forced-break path (`CssBox.PerformLayoutImp`/`CommitBlockChildOffset`) that
already honors a `page` name's used-value transition/reversion (issue #126). Closed for the table case;
flex/multicol remain open under a narrower follow-up (see the rewritten accepted-gap file).

## Load-bearing idea

Two independent bugs, both inside `CssLayoutEngineTable`, needed fixing together - the first alone
still left the second's symptom reproducible.

**1. No break on a row's own name transition.** `ForcedBreakFallsBeforeRow` decided a page break only
from `break-before`/`break-after` values. Extended it to also force a break when a row's *prospective*
used page name differs from `HtmlContainerInt.ActivePageName` - the same `pageNameChanged` rule
`CssBox.PerformLayoutImp`'s prologue already applies to ordinary block flow. Can't reuse
`CssBox.UsedPageName` directly for this, though: it's not populated yet at the point this decision has
to be made (only set inside a box's own `PerformLayoutPrologue`, which for a table row never runs before
this - see bug 2). `ProspectiveUsedPageNameFor` recomputes the same css-page-3 §3 used-value rule by
walking the row's own ancestor chain, grounded at `_tableBox.UsedPageName` (the table itself is an
ordinary block-flow box whose prologue has already run by this point). Once the break is decided, the
target slot's own geometry has to be resolvable immediately - mirroring block-flow's own "register the
used name before any child lays out" rule - so `PreRegisterPageNameTransition` registers the row's name
at the target slot's top *before* `TakeBreakBeforeRow` queries that slot's band height, setting
`row.RegisteredNamedPageElement` so the row's own later tail-fallback registration re-syncs against it
rather than duplicating it (exactly the pattern issue #149's table-relocation fix already established).

**2. A `<tr>` never gets `PerformLayoutPrologue` called on it at all** - only its cells do
(`LayoutBodyRow`'s own per-cell `cell.PerformLayout(g)` loop). `UsedPageName` therefore stays at
`CssBox`'s compile-time default (empty string) on every row, forever, regardless of the table's own
name. A cell then inherits from its row (`ParentBox?.UsedPageName`) exactly as the used-value rule says
it should - correctly by that rule, but wrongly in outcome, since it inherits the row's *never-set* empty
name instead of the table's real one. The cell's own prologue then computes a spurious
`pageNameChanged` (`"" != "wide"`) and registers a bogus reversion **mid-table**, corrupting
`ActivePageName` for whatever ordinary block-flow content came after the table - even content that never
touches the table engine at all. This is the "leaks past the table's own subtree" symptom the accepted-gap
doc had already isolated, and it reproduced independently of bug 1 (no row-level name transition needed
to trigger it - a single, unnamed row inside a named section was enough). Fixed by setting
`row.UsedPageName` from the same `ProspectiveUsedPageNameFor` walk at the top of `LayoutBodyRow`, before
any cell lays out, for *every* row (not just ones a break decision touches) - since every cell needs a
correct value to inherit from, not just cells on a transitioning row.

## What running it (not just reading it) confirmed

- A regression test for bug 1 alone (`NamedPageTransition_MidTable_ForcesABreakBetweenRows`) initially
  failed on `PageBandHeightOf(1)` (expected the named page's own full-sheet band, got the base band) even
  after the break-decision fix landed - the row DID move to the next slot, but that slot's geometry had
  already been resolved (and, per `PageGeometryTable`'s own forward-incremental/lazy design, effectively
  fixed) against the *previous* active name, because `TakeBreakBeforeRow`'s own `cursor.MoveToSlot` query
  ran before anything registered the new name. Confirmed by toggling the pre-registration call via
  `git stash` on just that one file: the test fails with exactly the wrong (previous-name) band height
  without it, passes with it.
- The subtree-leak fix (bug 2) was found by dumping every box's own `PageName`/`UsedPageName` after
  layout for the failing fixture (a temporary scratch test, removed before landing) - `<tr>`'s
  `UsedPageName` read as empty while `<table>`'s own correctly read the active name, immediately pointing
  at the missing per-row assignment rather than at anything in the registration/withdrawal logic the
  accepted-gap doc had originally guessed at ("the table engine's own registration bypassing the
  withdraw-on-exit path").
- Toggled bug 2's one-line fix (`row.UsedPageName = ProspectiveUsedPageNameFor(row);`) via a temporary
  comment-out and rebuild: the subtree-leak regression test fails with exactly the pre-fix (leaked) band
  height without it, passes with it - confirmed independently of bug 1's own fix.

## Deliberately not done

- Flex and multicol remain open (no equivalent per-child break hook exists in either engine to extend the
  way `ForcedBreakFallsBeforeRow` already existed for tables) - narrowed into its own follow-up issue,
  referenced from the rewritten `named-page-reversion-outside-block-flow.md`.

## Evidence

- New regression tests, each individually confirmed to fail pre-fix:
  `NamedPageGeometryAttributionTests.NamedPageTransition_MidTable_ForcesABreakBetweenRows` (bug 1) and
  `NamedPageGeometryAttributionTests.ReversionAfterNamedPage_TableInsideTheSection_StillReverts` (bug 2).
- `dotnet test PeachPDF.Tests/PeachPDF.Tests.csproj --framework net8.0` - full suite green (9897 passed, 9
  pre-existing platform-gated skips).
- `dotnet build PeachPDF.slnx -t:Rebuild` - 0 warnings.
