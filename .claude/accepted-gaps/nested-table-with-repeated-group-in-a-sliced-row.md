# A nested table with its own repeated `<thead>`/`<tfoot>` inside a sliced row

A table nested in a cell of an outer table's too-tall row breaks in the row's run frame (see
`.claude/recent-fixes/2026-10-27-nested-table-breaks-in-the-sliced-rows-run-frame.md`) **only when it repeats
nothing itself**. A nested table whose own `<thead>`/`<tfoot>` repeats keeps breaking at the real page edges
(`CssLayoutEngineTable.EnclosingGrid`), so under an outer repeated header its pages can still drift from page 3
on.

**Why.** An inner repeated group is a captured instance (`FragmentEmitter.RecordRepeatingGroupInstance`)
recorded against the real band. Run-frame coordinates plus the outer row's displacement did not reach it:
measured, the inner header was emitted twice at the page top, on top of the outer header, which is worse than
the drift it replaces.

**To close it:** make the emitter apply the enclosing row's displacement to a captured instance of a table inside
a displaced run (and record the instance's band in the run frame), then drop the `EnclosingGrid` guard.
Tracking issue: #1946 (css-tables-3 §6.2 repeated-header placement).
