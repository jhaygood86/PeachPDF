# Table height/min-height now enforced across a true row-loop continuation

[Issue #1116](https://github.com/jhaygood86/PeachPDF/issues/1116)'s measure-then-redo mechanism in
`CssLayoutEngineTable.PerformLayout` was gated on the table's row loop finishing entirely within a single
top-level pass (`resume is null` on entry, `PendingBreakToken` null after `Layout()` returns) — a table
whose row loop itself had to stop and hand control to a separate, later top-level pass (one cell's own
content needing a third fragmentainer) got no height enforcement at all, since the total natural extent
isn't known until the last pass completes and by then earlier rows are already committed/painted.
[Issue #1132](https://github.com/jhaygood86/PeachPDF/issues/1132) closes this, landing on top of
[#1131's](https://github.com/jhaygood86/PeachPDF/issues/1131) axis-generalization in this same PR (see
the sibling `2026-09-17-vertical-table-height-min-height-enforcement-closed.md` entry for that half).

## Banking a pass's own row-axis content LENGTH, not its far edge

The existing single-pass mechanism compares the table's declared far edge (`Location + resolvedSize`)
against `_naturalGridFarEdge`, both real absolute coordinates that cancel correctly within one pass. A
continuation's resumed pass, though, starts its own cursor at `ResumedRowTop` — the *page top* of
wherever the earlier pass's stop resumes — not the position the earlier pass's own rows actually reached.
Naively reusing the far-edge comparison across passes would silently count every page-boundary gap
crossed along the way as if it were more row content, over-stating the natural extent by however much
whitespace sits between pages.

`CssBox.NaturalRowAxisExtentCarry` (nullable `double`) instead accumulates a pure *length*: each pass's
own row-axis content contribution, measured from wherever *that pass's own cursor* began (captured as
`CssLayoutEngineTable._rowAxisPassStart`, right after cursor construction in `LayoutCells`) to
`_naturalGridFarEdge`, minus the table's own trailing row-axis border (`_naturalGridFarEdge` always
includes it, per pass, regardless of whether that pass turns out to be the table's true last one - so it
has to be added back exactly once, by the caller totalling multiple passes together, not once per pass).
`PerformLayout` banks this into the carry whenever a pass ends with `PendingBreakToken` still set (and no
redistribution/redo already in progress — a floored redo's own further continuation is a different,
already-decided situation with nothing left to bank), resets the carry only on a genuinely fresh
top-level entry (`resume is null`), and once a pass finally finishes, runs redistribution against
`carry + thisPassOwnLength + TableRowAxisBorderEnd` whenever a carry exists.

**Post-change review caught two real precision bugs here, both fixed in `ThisPassNaturalRowAxisContentLength`:**

1. The "content length" subtraction used `_rowAxisPassStart` (the pass's own cursor start)
   unconditionally, including for the *chain's first* pass. But the first pass is the only one that ever
   lays out a leading row-axis border, a top caption, or the border-spacing before the first row — all of
   which `_rowAxisPassStart` (`startY` there) already includes — so subtracting it discarded that whole
   leading offset from the banked total. Fixed by subtracting the table's own true row-axis-start
   coordinate instead, but only for the pass where `!_continuesAPreviousPass` (the chain's first); a later
   continuation never repeats any of that leading geometry, so its own `_rowAxisPassStart` is exactly
   right for it.
2. `LayoutBodyRows`' own Step 7 unconditionally adds a single trailing row-axis gap
   (`VerticalSpacingAt(_grid.RowCount)` — for a non-collapsed table, simply the flat `border-spacing`
   value) into `_naturalGridFarEdge`, on *every* pass, regardless of whether that pass is genuinely the
   table's last one. Summing more than one pass's own (otherwise-correct) contribution therefore
   double-counted this gap once per extra pass. Fixed the same way as the trailing border: subtracted out
   of every pass's own contribution, added back exactly once by the caller totalling the chain together.

Both are verified correct algebraically against the pre-existing, already-tested single-pass formula (the
carry-based total reduces to it exactly when there is no carry), and a border-spacing regression test
(`TableHeight_TrueRowLoopContinuation_WithBorderSpacing_RedistributesWithoutCorruptingEarlierRows`) proves
the mechanism still redistributes correctly without throwing or corrupting earlier rows once spacing is
present. What that test deliberately does **not** assert is an exact expected magnitude for the spacing
case: getting one requires precisely modeling how a stopped row's own natural height is recorded mid-row
loop (a separate, pre-existing piece of machinery neither issue touches), which turned out to be
non-trivial to reverse-engineer by reading alone — attempted, and abandoned once it became clear that
pinning it down further was a distinct, pre-existing area of the row loop rather than something this
fix's own algebra gets wrong. Recorded as its own accepted gap
(`.claude/accepted-gaps/table-height-continuation-border-spacing-caption-precision.md`, tracked as
[issue #1195](https://github.com/jhaygood86/PeachPDF/issues/1195)) rather than left as a silent test gap,
per this repo's own convention for a deliberately-scoped-out spec deviation.

## The redo itself needed no new mechanism — only reusing its own `resume`

Scoping a continuation's redo to only the rows the final pass placed needed no new machinery at all:
reusing the *same* `resume` the invocation itself received for the redo call (rather than always
hardcoding `resume: null`) is what does it. A non-null resume re-enters the row loop at exactly the row
index the original stop named — every earlier, already-committed/painted row is a different `CssBox`
instance from a different point in time that the resumed row loop never revisits. The proportional
surplus is still computed over every row in `_bodyRows` (including already-committed ones) exactly as in
the single-pass case; an unused floor entry for a row the redo will never touch is harmless, since only
the redo's own row-by-row `RowHeightFloor` lookups ever consume the dictionary.

## This is a best-effort fix, deliberately, and the docs say so

Because already-committed rows are never revisited, the table's overall realized row-axis extent can
still fall short of the declared `height`/`min-height` in this specific scenario — by however much of
the surplus those earlier rows would otherwise have taken. This is exactly what issue #1132's own scope
section anticipated ("accepting that the final page's rows may need a larger ... share than earlier pages
already committed to") rather than a residual bug, and `docs/html-css-support.md`'s own height/min-height
row on `<table>`/`<tr>` is worded to say so explicitly, rather than implying the declared size is always
reached exactly the way it is for a single-pass or ordinary multi-page table. Post-change review also
noted that the `_isVertical` branches this mechanism's own axis-aware helpers carry are, today, never
actually reached for a *vertical* table specifically in the continuation case: a vertical table's own row
loop runs with `pageHeight` forced to `double.MaxValue` (see `.claude/invariants/` and the writing-mode
remarks atop `CssLayoutEngineTable`), so it never produces a genuine `PendingBreakToken` mid-table in the
first place. Left in rather than special-cased away, since the axis selection is otherwise uniform with
every other row-axis-aware computation in this file, and a future change that does give a vertical table
real per-row pagination would need it working already.

## What was found by running it, not by reading it

Testing this required directly driving `CssLayoutEngineTable.PerformLayout` twice in a row via the
existing `TableRowLoopResumptionTests` pattern (`StopRow`/`RunEngine` with a detached fragmentainer) —
stop row 2's cell, capture the resulting continuation, restore the real cell, and resume. The second
`RunEngine` call kept reporting a stop at the *same* row 2, with `table.PendingBreakToken` still holding
the *first* pass's own stale token, even though the restored cell's own layout had genuinely finished.
`RunEngine` calls the engine directly, bypassing `CssBox.PerformLayoutImp`'s ordinary prologue
(`BeginLayoutPass`), which is what clears a box's `PendingBreakToken` at the start of each real pass in
production — a test-harness-only quirk this fix's own code never hits in practice (production always
goes through that prologue before reaching this engine), but one the test had to clear by hand
(`table.SetPendingBreakToken(null)`) between its two manual calls to actually exercise a genuine second
pass.

A second false start: the test's first attempt measured a row's "natural" pre-redistribution height by
reading it immediately after the pass that *stopped* on it — but a row whose only cell reported
`PendingBreakToken` never reaches the line that sets its own row-axis extent, so that reading was
spuriously near zero regardless of the fix's own correctness. Fixed by asserting against a simple
absolute bound (well under an ordinary one-line row's real height) for the un-redistributed control case,
rather than resting the positive case's own "it grew" assertion on that same unreliable baseline.

## Evidence

Three new tests in `TableRowLoopResumptionTests.cs`: a true row-loop continuation with an explicit table
height, asserting the floor is applied only to the final pass's own remaining rows (2 and 3) while rows 0
and 1 — already committed by the pass that stopped — are asserted byte-unchanged; a no-explicit-height
control proving zero behavior change when the carry mechanism is exercised but enforcement doesn't apply;
and the border-spacing regression test described above. Full `PeachPDF.Tests` suite green on net8.0
(12,363 passed, 0 failed, 9 skipped — pre-existing platform-gated MIME-type tests). Diff coverage 100% via
`diff-cover` against `origin/main`. `dotnet build -t:Rebuild` on the whole solution is warning-free.
