# `RowHeightRedistribution`/`NaturalRowAxisExtentCarry` reset on a mid-layout throw

`CssLayoutEngineTable.PerformLayout` only reset `CssBox.RowHeightRedistribution` and
`CssBox.NaturalRowAxisExtentCarry` to `null` on the success path, gated on `tableBox.PendingBreakToken`
being `null` at the very end of the method's `try` block. `CssBox.TableContinuation` gets the equivalent
treatment differently: it is cleared unconditionally at the top of the engine's constructor, "before
anything can throw" (see that field's own comment), specifically so a run that dies part-way never leaves
a stale answer behind for whatever re-enters the box next. The other two fields never got that same
treatment - a gap already latent for `RowHeightRedistribution` alone since issue #1116, and extended to
`NaturalRowAxisExtentCarry` by issue #1132's true row-loop-continuation fix without addressing the
underlying robustness hole.

## Why the constructor's own unconditional reset doesn't fit here

The load-bearing reason this isn't just "copy `TableContinuation`'s reset into the constructor": unlike
`TableContinuation`, both fields are *deliberately* carried forward across the several top-level passes a
genuine row-loop continuation needs (see `PerformLayout`'s own remarks on
`NaturalRowAxisExtentCarry`/`RowHeightRedistribution` immediately above where they're read/written).
Resetting either one at the top of every constructor call would wipe out a mid-chain continuation's own
accumulated state on its very next pass - the opposite of the bug. The only safe place to intervene is the
`catch` block: it already runs exactly once, whenever - and only when - a pass genuinely fails, without
touching the success-path carry-forward logic at all.

## Fix

Added a `tableBox.RowHeightRedistribution = null; tableBox.NaturalRowAxisExtentCarry = null;` pair to the
existing `catch (Exception ex)` block in `PerformLayout`, before it rethrows via `container.RenderError`.
No change to the success-path logic (the two conditions already covering `PendingBreakToken is null` and
"gated on `RowHeightRedistribution is null`" ahead of a redo) - this only closes the path where an
exception previously skipped both resets entirely.

## Tests

`TableRowLoopResumptionTests.ARunThatDies_LeavesNoRedistributionStateFromThePreviousOne`, built on the
existing `ARunThatDies_LeavesNoRecordFromThePreviousOne` template (`StopRow`/`ThrowingCell`/`RunEngine`
with a detached fragmentainer): runs a first pass that stops (banking a real
`NaturalRowAxisExtentCarry`), manually seeds `RowHeightRedistribution` to simulate the shape described in
the bug (a pass whose own genuine redistribution work left it set too), replaces the row the continuation
would resume into with a cell that throws, and asserts both fields are `null` after the exception
propagates through a continuation-shaped re-entry (non-null `resume`).

## Evidence

`dotnet test PeachPDF.Tests/PeachPDF.Tests.csproj --framework net8.0`: full suite green (12,499 passed, 9
pre-existing platform skips, 0 failures), including the 42 tests in `TableRowLoopResumptionTests.cs`.
Diff coverage on the two changed/added production lines: 100% (confirmed directly against the Cobertura
report - `diff-cover` itself wasn't available in this environment). `dotnet build PeachPDF.slnx -t:Rebuild`:
0 warnings across the whole solution.
