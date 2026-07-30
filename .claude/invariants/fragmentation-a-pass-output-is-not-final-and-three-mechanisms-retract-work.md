# A pass's output is not final, and three mechanisms now retract work

_CSS Fragmentation Level 3. Tracker: [#320](https://github.com/jhaygood86/PeachPDF/issues/320)._

Three mechanisms now retract work: `FragmentEmitter.InvalidateFrom`, `DiscardLineBoxesFrom`, and
`PassRewind.RollBackTo` — the last one shared by #355's pass re-entry, #371's rewind, and the
multi-column engine's own abandoned-fill retry, which used to wrap it in a fourth name
(`ResetChildrenForRefill`) that added nothing but a wrapper
([#516](https://github.com/jhaygood86/PeachPDF/issues/516)). `TryRestartAt`'s own same-pass
restart needs none of the three: it hands the run's head a target through the same `ResumeAt`
channel an ordinary fragmentainer resumption uses, then re-enters the parent's child loop from
there — every box from the head on gets a fresh `PerformLayoutImp` call exactly as an
unreached child would, and `BeginLayoutPass` resets `_earlyBreakTaken` for each one on that entry
regardless of who calls it, so there is nothing left to retract by hand first. That is what "the
parent holding the offset" (`PlaceBlockChild`) buys a same-pass restart over a cross-pass one: the
head's *position* is resolved the same way whether re-appended into a still-open pass or rewound
into a completed one, and only the completed case needs a real rollback, since only there has the
box's own state (a completed prologue, finalized line boxes) actually been settled onto record.

Undoing an attempt on a *resumed* pass is a three-way question — below the resume point, at it, and
above it — and "resumed" is not one state. Retracting lines is not retracting geometry: the words
have to go too (`AwaitsTheNextFragmentainer`, which is self-healing). **The invariant that catches
both directions of getting any of this wrong is one line over the fragment tree: every word claimed
exactly once.**
