# A box's prologue runs once per *layout*, so a re-entered pass does not re-run it

_CSS Fragmentation Level 3. Tracker: [#320](https://github.com/jhaygood86/PeachPDF/issues/320)._

`CssBox.PerformLayoutPrologue` is guarded by `_prologueDone`, which `BeginLayoutPass` clears only when the **layout generation** changes — not per fragmentainer pass. Everything the prologue settles is therefore settled exactly once for the whole document, and a pass the driver re-enters ([#355](https://github.com/jhaygood86/PeachPDF/issues/355)'s keep-with-next rewind, the columns engine's abandoned fill attempt) gets **none of it back**:

- `RectanglesReset` does not run, so the box keeps every per-line rectangle the discarded passes gave it. Combined with `CssLayoutEngine.CreateLineBoxes` not clearing a *resumed* box's line list, `FinalizeLineBoxes` hands the same `CssLineBox` to `AssignRectanglesToBoxes` twice and throws `An item with the same key has already been added` ([#415](https://github.com/jhaygood86/PeachPDF/issues/415), and [#374](https://github.com/jhaygood86/PeachPDF/issues/374) before it from the columns side).
- A **forced break** is taken there, so a box whose prologue ran in a discarded pass never takes its break again — `break-before: page` silently does nothing after a run pull ([#434](https://github.com/jhaygood86/PeachPDF/issues/434)).
- `string-set` and named-page registrations are applied there, which is why the prologue withdraws its own previous registrations before registering again.

Anything that re-runs a pass must therefore roll the box tree back explicitly — `PassRewind.RollBackTo`, which walks the resumption record and lets the prologue back in (`ResetForRefill`) for the children the pass lays out from the start. Note what that does **not** reach: `ResetForRefill` clears `_prologueDone` on the box it is called on and not on its descendants, and a rewind to the *first* pass carries a null record and so names nothing at all.
