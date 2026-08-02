# A fan-out break token's per-child continuations are now walked, not just its own top-level box

Closes [#590](https://github.com/jhaygood86/PeachPDF/issues/590).

## What was actually wrong

`FragmentEmitter.RecordChain` (`FragmentEmitter.cs`) is what tells a materialized `BoxFragment` whether
its own top/bottom edge is a real box edge or a fragmentation break — it walks the incoming/outgoing
`BreakToken` of each pass and records `(box, slot)` into `_continuedFrom`/`_continuesInto`, which
`Draft.ContinuedFromThePrevious`/`ContinuesIntoTheNext` (and from there `HasTopEdge`/`HasBottomEdge`) read
back. Before this fix it only understood a linear chain — `BlockBreakToken.Box` → its one continuing
child's own token, and so on via `ChildToken` — which is the right shape for ordinary block flow but wrong
for `TableBreakToken`/`FlexBreakToken`/`GridBreakToken`/`FlexColumnBreakToken`: each of those names its
*container* box only and fans out into several §2.1 parallel-flow continuations at once (`UnfinishedCells`,
`UnfinishedItems`, `UnfinishedLines`), which `RecordChain` never descended into. The result: a table cell
(or flex/grid item) whose own content genuinely produced more than one *real* per-pass fragment reported
`HasTopEdge: true`/`HasBottomEdge: true` on every one of those fragments, not just the first/last — so
`box-decoration-break: slice`'s border/background repainted on every page the cell's content spanned
instead of being sliced. A cell whose continuation is a *stated shell* (`FragmentEmitter.RecordContinuationShell`)
was unaffected, since the edge predicates already accept `Draft.ShellRect is not null` as an alternate
signal; the gap was specifically a cell/item that keeps producing real content fragments pass after pass.

Found while fixing #521 (rowspan cell content overflow) — confirmed independent of rowspan on a plain,
non-rowspan multi-page `<td>`, and filed separately rather than folded into that fix.

## The fix

Added a `virtual IReadOnlyList<BreakToken> FanOutContinuations => []` member to the abstract `BreakToken`
record, overridden by each of the four fan-out token kinds to expose its own per-child tokens
(`UnfinishedCells.Select(c => c.Token)`, etc. — `UnfinishedLines.Select(l => l.ItemToken)` for the column
shape). `RecordChain` now recurses into `link.FanOutContinuations` for a non-`BlockBreakToken` link instead
of stopping. This is safe and correct without any extra bookkeeping because every one of these per-child
tokens is `childBox.PendingBreakToken` at the point it was collected (confirmed at every construction site
— `TableRowCursor.RecordIfUnfinished`, `CssLayoutEngineFlex.CommitLineContent`/`CommitColumnContent`,
`CssLayoutEngineGrid.CommitRowContent`), and `CssBox.PendingBreakToken` is always set as `new
XxxBreakToken(this, ...)` — so a child token's own `Box` already names the child itself, exactly the
invariant `RecordChain`'s existing `into.Add((new FragmentKey(link.Box, null, 0), slot))` at the top of the
loop depends on. No separate "mark the cell's own box" step was needed. The recursion also handles nesting
for free — a fan-out token whose own per-child continuation is itself a further fan-out (a table nested in
a table cell, say) walks correctly, since `RecordChain` is called on that continuation exactly as on any
other token.

## What was deliberately not done

- No `(CssBox, BreakToken?)` tuple shape, despite the issue's own "What it would take" section suggesting
  one. It isn't needed: since every fan-out continuation's `Token.Box` already equals its own child box (see
  above), a plain `IReadOnlyList<BreakToken>` is sufficient — `RecordChain`'s existing per-link `Box` read
  already recovers the child.

## Evidence

- New tests: `TableCellOwnContentBreakEdgesTests.cs` (`ACellsOwnContentSpanningSeveralBands_OwnsOnlyTheBlockEdgesTheBreakDidNotMake`,
  with `ACellsOwnContentFittingOneBand_OwnsEveryEdgeOnItsOneFragment` as the control against a
  vacuously-passing single-fragment fixture) — both verified to fail without the fix
  (`ACellsOwnContentSpanningSeveralBands...` specifically: `Assert.Equal(false, slice.HasTopEdge)` on the
  second fragment failed with `Actual: True` on `origin/main`).
- `TableRowspanContinuationTests.ASpanningCellWhoseContentOverflowsItsBand_IsFragmentedAcrossEveryBandItOccupies`
  — the comment recording this gap as out-of-scope (added by #521's own fix) replaced with real
  `HasTopEdge`/`HasBottomEdge` assertions across every real fragment; passes.
- Full `net8.0` suite: 7526 passed, 0 failed, 9 skipped (platform-gated, pre-existing).
- `diff-cover` against `origin/main`: 100% on the changed lines.
- `dotnet build PeachPDF.slnx -t:Rebuild`: 0 warnings.
