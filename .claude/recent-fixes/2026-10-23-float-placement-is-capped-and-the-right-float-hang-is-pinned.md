# Float placement is capped, and the right-float negative-margin hang (#1815) has tests

The cause #1815 names (`FloatBoxRight` building its `CssFloatCoordinates` without `MarginTop`) was already fixed on
`main` by the `OuterTop`/`DropTo` change: the issue's own document finishes in about 2.5 s through the CLI. What was
missing was any test of that shape, so this adds them and a backstop.

- `FloatLayoutRegressionTests`: the issue's document in points (a right float with a negative `margin-top` that
  cannot sit beside a wider left float), a blocker with a negative `margin-bottom` on either side, and a 100-document
  corpus of every pairing of float sides and one negative margin per float. Each runs under a 60 s timeout.
- `CssLayoutEngine.MaxFloatPlacementAttempts` (10 000): both `FloatBoxLeft` and `FloatBoxRight` stop searching after
  that many blockers and leave the float where it is. `DropTo` already makes every drop move further down, and the
  `triedPositions` set breaks a repeated state, but that set compares doubles for exact equality; the cap means a state
  neither argument covers gives a wrong position, not a layout that never ends.

Not done: `FloatBoxLeft` still drops to the blocker's border-box bottom, where `FloatBoxRight` clears its margin box
(CSS 2.1 section 9.5.1 rule 8). That moves floats for documents that never hung, so it stays a separate change.
Supersedes the "`FloatBoxRight` was not touched" remark in
[2026-09-30-left-float-negative-margin-top-hang.md](2026-09-30-left-float-negative-margin-top-hang.md) and the open
hang bullet in the float gap note under `.claude/accepted-gaps/`.
