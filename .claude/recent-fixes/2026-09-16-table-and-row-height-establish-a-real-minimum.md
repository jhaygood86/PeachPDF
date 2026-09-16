# `<table>`/`<tr>` `height`/`min-height` now establish a real CSS 2.1 §17.5.3 minimum

[Issue #1116](https://github.com/jhaygood86/PeachPDF/issues/1116): a `height`/`min-height` on a
`<table>` or `<tr>` had no effect — the table/row laid out at content height regardless. `<td>` and
plain block elements already honored it. The visible symptom was `vertical-align` on a cell reading as
a no-op: the row was always exactly one line tall, so `top`/`middle`/`bottom` landed in the same place.

**Root cause was two separate gaps, not one.** `CssLayoutEngineTable.LayoutBodyRow`'s row-height
candidate loop never read `row.Height`/`row.MinHeight` at all — `CssLayoutEngine.ApplyParentHeight`
correctly skips the generic height applier for a table row (row height is meant to be owned by the
table engine), but nothing in the table engine ever picked it up in the applier's place. And the
generic `ApplyHeight(this)` call `CssBox.PerformLayoutEpilogue` already makes on a table box ran *after*
`CssLayoutEngineTable.Layout()` had finished — so it only rewrote `ActualBottom`, a bookkeeping field,
after row/cell/collapsed-border geometry was already finalized from content alone. Nothing re-flowed
into the extra space, which is why the property read as having no effect at all even though a number
was technically being assigned somewhere.

## The load-bearing idea: measure, then redo with computed per-row floors

Column-width surplus (`SpreadSurplusProportionally`) can be decided *before* layout, because an auto
column's intrinsic width is knowable from a measurement pass ahead of placing content. A row's natural
height cannot be predicted the same way — it is a genuine result of laying out real content against
already-fixed column widths. So the target-vs-natural comparison can only happen after a real layout
pass has already run.

Rather than hand-patch already-finalized row/cell/border/caption/header-footer-proxy geometry after the
fact, the fix reuses the engine's own existing "laid out again, fresh from markup" mechanism (see
[`.claude/invariants/fragmentation-a-table-is-laid-out-again-for-four-reasons-and-only-one-of-them-continues-it.md`](../invariants/fragmentation-a-table-is-laid-out-again-for-four-reasons-and-only-one-of-them-continues-it.md)):
`CssLayoutEngineTable.PerformLayout` runs `Layout()` once normally, and — only when the table completed
entirely within that pass (`resume is null`, `PendingBreakToken is null` afterward) and its resolved
`height`/`min-height` exceeds what its rows naturally reached — computes each row's proportional share
of the shortfall (`TryComputeRowHeightRedistribution`, mirroring `SpreadSurplusProportionally`'s own
formula on the row axis) and runs `Layout()` a **second time**, this time with those per-row floors
available through `CssBox.RowHeightRedistribution`. `LayoutBodyRow`'s existing per-row `Math.Max`
candidate loop (the same place a genuine `<tr height>` now feeds in too, via `RowHeightFloor`) picks
them up like any other row-height candidate.

Because the redo is a complete, fresh re-run — not a post-hoc patch — every downstream computation that
already derives correctly from row geometry (rowspan band-closing, collapsed-border grid lines,
captions, header/footer proxies, per-row page-break decisions) is correct on the second pass for free:
it is the same, already-tested code, now simply seeing taller rows.

## What was found by running it, not by reading it

The first version gated `RowHeightFloor`/`TryComputeRowHeightRedistribution` on
`CssValueParser.IsValidLength(row.Height) || CssValueParser.IsValidLength(row.MinHeight)` — reasonable
by analogy with the existing cell-height code, but wrong: **`min-height`'s initial value is the literal
string `"0"`**, not `"auto"` (`css-properties.json`), so that check is true for *every* row and every
table whether or not an author ever wrote it. That made `RowHeightFloor` call
`CssLayoutEngine.GetBoxHeight(row)` unconditionally on every ordinary row - and a `<tr>` is never given
its own `PerformLayoutPrologue` call, so `GetBoxHeight`'s auto-height fallback baseline
(`ActualBoxSizingHeight`, i.e. `Size.Height + ActualBoxSizeIncludedHeight`) can hold a stale value the
same `CssBox` picked up from an unrelated earlier layout of some other box kind. It did not throw or
produce an obviously-wrong number — it grew `rowMaxBottom` by a few points on rows with no explicit
height at all, which only showed up as pagination-decision drift in tests calibrated to exact geometry
(`TableRowspanContinuationTests`, `TableCellBreakTokenTests`, `TableRepeatedGroupConditionsTests` — 13
failures across the full suite, none of them about explicit height). Fixed by additionally requiring
`row.MinHeight != "0"` (and the mirrored check on the table's own `MinHeight` in
`TryComputeRowHeightRedistribution`, though there `GetBoxHeight(_tableBox)` was always safe to call
since a table *does* go through prologue — the surplus check below it already prevented a false
trigger, so that half was a wasted-cycles fix, not a correctness one).

A separate, complementary fix landed alongside this: `CssLayoutEngine.ApplyHeight`'s existing
"never shrink below content" carve-out (already applied to table cells,
[`2026-08-13-table-cell-explicit-height-smaller-than-content.md`](2026-08-13-table-cell-explicit-height-smaller-than-content.md))
was extended to `display: table`/`inline-table` too — §17.5.3 makes the table's own height a
maximum-of rule, same as the cell's, so an explicit `height` smaller than the rows' real content must
never clip the table's own `ActualBottom`.

## A defensive fix from the post-change review pass, not empirically reproduced

The review pass raised a real question about `PerformLayout`'s own bookkeeping: the first version
cleared `tableBox.RowHeightRedistribution` unconditionally in a `finally` right after the redo call
returned. If the *redo* pass itself needed a genuine mid-cell continuation (`cursor.Stopped`) - distinct
from the ordinary per-row page-break case the pagination test above covers - the floors would be wiped
before the later top-level pass that resumes the remaining rows could read them, leaving everything
after the stop point at bare natural height.

Fixed by deferring the clear until `tableBox.PendingBreakToken is null` (the table is genuinely,
fully done, on whichever call that happens), rather than unconditionally after the redo. This is a
correct hardening regardless, but attempting to actually construct a fixture that exercises it turned
up something worth recording: a row's own bookkeeping height (whether from an explicit `<tr height>` or
from a computed redistribution floor) is applied to `rowMaxBottom` **after** every cell in that row has
already finished its own content layout - so it can never retroactively make a cell decide it needs to
continue. And content that fits within one page always resolves via straddle correction relocating the
whole row to a fresh page, regardless of how far redistribution displaced its starting position. Every
fixture tried either triggered `cursor.Stopped` on the *natural* pass too (already excluded by the
existing gate, so redistribution never starts) or resolved cleanly via relocation on the redo pass.
Left in as defense-in-depth for a path not fully ruled out (repeating groups, rowspan-crossing
interactions, or future changes to the row loop) rather than a confirmed-reachable bug.

## Deliberately not done

- **A table whose row loop itself must continue into a separate, later top-level layout pass**
  (`PendingBreakToken` still non-null when the natural pass returns — one cell's own content needing a
  third fragmentainer) gets no height enforcement. Its total natural height across every pass isn't
  known until the last of them completes, and by then earlier rows are already committed/painted and
  cannot be redone. Ordinary multi-page tables (which take per-row breaks *inside* one
  `LayoutBodyRows` call) are unaffected by this carve-out and are fully handled — the redo pass's own
  row loop re-runs its own page-break decisions against the grown rows, not a copy of the first pass's.
- **`writing-mode: vertical-rl`/`vertical-lr` tables.** `height`/`min-height` are physical properties;
  on a vertical table the row axis (where §17.5.3's rows stack) is physical *width*, not height —
  `_isVertical`'s own Step 7 branch already routes genuine CSS height to the *column* axis there. A
  vertical table's row-axis-minimum analog would be `width`/`min-width`, entangled with the table
  engine's separately-working column-width algorithm — a distinct, symmetric gap, not this one.

Both are recorded as accepted gaps with tracking issues rather than silently dropped.

## Evidence

Seven new unit tests in `CssLayoutEngineTableTests.cs` (`<tr>` height/min-height, `<table>`
height/min-height growing and not-shrinking, the issue's own three-cell vertical-align repro, two-row
proportional distribution, a rowspan cell crossing a redistribution-grown row) and one new pagination
test in `CssLayoutEngineTablePageBreakTests.cs` (explicit table height pushing a row across a page
boundary it would not cross at natural height). The issue's own repro re-rendered via the CLI and
rasterized through both PDFium and MuPDF, confirming three distinct `top`/`middle`/`bottom` label
heights matching Chrome. Full `PeachPDF.Tests` suite green on net8.0 (12,058 passed, 0 failed — the
`FontVariantLigaturesIntegrationTests` failures seen mid-investigation were pre-existing
`PeachPDF.Fonts.FontFactory` static-cache order-dependent flakiness (CLAUDE.md already warns this class
of test is a "real order-dependent-flakiness risk"), confirmed unrelated to this change by passing in
isolation both with and without it). `dotnet build -t:Rebuild` on the whole solution is warning-free.
