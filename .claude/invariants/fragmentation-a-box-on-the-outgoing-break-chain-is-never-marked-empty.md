# A box on the outgoing break chain is never marked "emitted nothing"

`FragmentEmitter` prunes a subtree it has seen empty (`CssBox.RecordEmittedNothingAt` /
`EmittedNothingAtOrBefore`), on the claim that its fragments are all behind the slot where the mark was
made. A box the pass stopped inside (on the outgoing `BreakToken` chain, recorded in `_continuesInto`) still
has content ahead of it, so it must never be marked, whichever commit path writes the mark:
`CommitGeometricallySettledObservations` and `CommitRemainingObservations` both skip it.

Such a box can look empty for a whole pass: when a mover sends layout back to the slot the pass has just
filled, the pass places nothing there and ends with a token resuming in that same slot. A mark written then
is not discarded by the re-run, because a box laid out again at the same position fires no
`DiscardEmittedNothing`.

**Measured symptom:** an auto-height `overflow: hidden` card starting within its first child's
`margin-top` of the page foot drew only its last line and what followed it; `html` and `body` were marked
empty at the slot the card moved to, and two pages were pruned to nothing
(`MonolithicContentLayoutIntegrationTests.CardWhoseFirstChildsMarginReachesPastThePageFoot_DrawsEveryLineOnce`).
`PEACHPDF_VERIFY_FRAGMENT_PRUNING=1` catches this class of bug as "the root draft is 'null' with pruning and
'a draft' without it"; run a suspect document with it before reading emitter code.
