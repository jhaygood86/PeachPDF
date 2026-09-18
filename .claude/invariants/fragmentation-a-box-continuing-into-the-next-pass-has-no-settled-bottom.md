# A box continuing into the next pass has no settled bottom

`CssBox.ActualBottom` is a fact about a box that has *finished* laying out. A box only reaches its epilogue
(where its height is applied) on the pass that completes it, so a box the current pass stops inside - one
named by the pass's outgoing `BreakToken` chain, `FragmentEmitter._continuesInto` - carries a stale,
understated bottom until a later pass finishes it. Anything that reads `ActualBottom` to conclude "this box
is done, it ends above slot N" must exclude those boxes.

The one place that does is `CommitGeometricallySettledObservations`, which turns that reading into an
irreversible "emitted nothing" mark that `BuildDraft` later uses to prune every slot from there on. Its
`continue` condition therefore includes `_continuesInto.Contains((new FragmentKey(box, 0), slotIndex))`.
Removing it, or replacing the exact chain lookup with a geometric guess, re-opens the defect.

The measured symptom: a `break-before: recto` chapter whose first page ends mid-paragraph. The directional
break reserves the skipped slot as a blank page, so one `EmitPass` sweeps the cover, the blank slot and the
chapter's first page together; the pass ends with `<html>`/`<body>` in the outgoing chain and an
`ActualBottom` of about 91pt (real: about 4200pt); the empty blank slot "proves" them done; the rest of the
document is pruned. The showcase `paged_media_directional_breaks` lost its Chapter 1 heading and paragraphs
1-5 and rendered 8 pages instead of 9, and chapters opened on the wrong side of the sheet.

**A test on where a box landed, or on one heading's text, does not catch it** - later content can land
correctly while earlier content vanishes. Assert the whole ordered word list and slot contiguity. And the
fixture has to make a pass end *inside* a box within a sweep that includes a reserved blank slot: fixed-height
blocks that finish on their landing page never do, which is why the existing directional-break tests all
passed against it.
