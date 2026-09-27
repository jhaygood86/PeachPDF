# A pass that resumes inside its own range commits no "emitted nothing" marks

`FragmentEmitter` prunes a subtree it has seen empty (`CssBox.RecordEmittedNothingAt` /
`EmittedNothingAtOrBefore`), on the claim that its fragments are all behind the slot where the mark was
made. `EmitPass` commits the leftover observations of a pass only once its whole range is walked
(`CommitRemainingObservations`), and not when the pass's outgoing record resumes inside that range
(`outgoing.ResumeSlotIndex <= throughSlot`). That happens when a mover sends layout back to lay a box out
again in a slot the pass has just filled: the pass may have placed nothing there at all, so everything on
the break chain looks empty, and a box laid out again at the same position fires no
`DiscardEmittedNothing` to clear a wrong mark.

**Measured symptom:** an auto-height `overflow: hidden` card starting within its first child's
`margin-top` of the page foot drew only its last line and what followed it; `html` and `body` were marked
empty at the slot the card moved to, and two pages were pruned to nothing
(`MonolithicContentLayoutIntegrationTests.CardWhoseFirstChildsMarginReachesPastThePageFoot_DrawsEveryLineOnce`).

**Do not fix this by skipping the marks of boxes on the outgoing break chain instead.** That was tried: it
fixes the card, but the other pruning shortcuts (the live-child cursor, the stop in `DiscardEmittedNothing`'s
upward walk) assume the marks an ordinary pass writes, and on a plain document holding a float, a
`flow-root` box and a flex container the flex item's last line was drawn on no page
(`PlainDocumentWithAFloatHoldingAFlexContainer_DrawsEveryWord`). Across 1,782 fuzz documents with no scroll
container, that version changed 13 (4 of them for the worse); the resume-inside-the-range rule changes 1,
for the better.

`PEACHPDF_VERIFY_FRAGMENT_PRUNING=1` catches this class of bug as "the root draft is 'null' with pruning
and 'a draft' without it", but some documents already diverge on `main` (the float/flow-root/flex one above
does), so compare what is drawn against a run with pruning off rather than trusting the check alone.
