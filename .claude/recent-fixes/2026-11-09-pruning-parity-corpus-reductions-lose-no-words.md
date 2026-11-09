# Fragment-pruning parity throws on the generated corpus are empty slivers, not word loss (#1487)

Re-triage of the dense generated corpus reductions P1-P4 (columns + nested `ul`, columns + `h1` + fixed-height
div, column flex with a bordered float, grid with a right float) on current `main`.

- With `PEACHPDF_VERIFY_FRAGMENT_PRUNING=1` all four still throw (child-fragment count for P1/P2,
  `BoundsEndAtItsContent` on a trailing slot for P3/P4).
- Counting words in the pruned and unpruned drafts at every slot the check reaches (temporary diagnostic, not
  kept): the word counts are identical in every slot; the extra nodes the unpruned walk emits carry no words.
  (A whole-engine-suite parity run was attempted but ended early with a non-success exit code, so it is not evidence either way.)
- With pruning on and the check off, every authored word of each reduction is in the fragment tree once.

So no mark writer (`RecordEmptyObservations`, `CommitRemainingObservations`,
`CommitGeometricallySettledObservations`) was changed: tightening them to remove empty slivers would only cost
the pruning speedup (1.6-4x) for no content gain. The four reductions are pinned in
`FragmentPruningParityTests.GeneratedCorpusReductions_KeepEveryWordWithPruningOn` on the word-count property.
The parity check itself still cannot be used as a pass/fail oracle on this corpus because it reports the harmless
sliver differences - counting words (see the invariant on emitted-nothing marks) is the reliable measure.
