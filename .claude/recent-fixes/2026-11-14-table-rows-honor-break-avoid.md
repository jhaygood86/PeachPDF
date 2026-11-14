# Table rows honor `break-after/before: avoid` and a row group's `break-inside: avoid`

_Landed 2026-11-14 (issue #2410)._

The table row loop (`CssLayoutEngineTable.LayoutBodyRows`) only read *forced* breaks between rows and a
row's own `break-inside: avoid`, so a group heading row with `break-after: avoid` could end a page while its
first data row started the next. Fixed by answering the avoid **after the later side lands** (see
`.claude/invariants/fragmentation-an-avoided-break-is-answered-after-the-later-side-has-landed.md`):
both break arms (the estimate arm and the straddle/avoid correction) ask `BreakBeforeRowIsAvoided` and, if so,
`AvoidRunHead` finds the earliest row of the avoid chain placed in the same band; `TakeBreakBeforeRun`
retracts rows head..i-1 and takes the break before the head, then the loop restarts at the head.

Load-bearing details:
- Relaxation (§4.4) is by trimming the run from the front: a head that begins its band (or the resumed row) is
  never moved (no empty page, guarantees progress: after the move the head begins its band), and a run that
  does not fit `RoomForARowIn(next band)` is trimmed until it does.
- After a straddle correction the row's `RowPlacement` is re-taken (`cursor.BeginRow()`), otherwise a later pull
  would restore `MaxBottom` to the old band.
- Forced break at the same point wins (`BreakBeforeRowIsAvoided` returns false when `ForcedBreakFallsBeforeRow`).
- Rowspans: `TableRowCursor` keeps foreign writes to spanning cells across rows (`RowPlacement.ForeignMark`), so
  retracting several rows undoes every row's writes; `TakeBreakBeforeRun` also clears their continuation shells.
- Reference: Chromium does the same via early-break relayout; WebKit has no row avoid.

Evidence: `TableRowBreakAvoidIntegrationTests` (fail without the change: 6 of 12), engine suite 19955 and
PeachPDF.Tests 1973 pass.
