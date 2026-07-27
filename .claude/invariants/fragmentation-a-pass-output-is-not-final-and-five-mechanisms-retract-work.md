# A pass's output is not final, and five mechanisms now retract work

_CSS Fragmentation Level 3. Tracker: [#320](https://github.com/jhaygood86/PeachPDF/issues/320)._

Five mechanisms now retract work: `FragmentEmitter.InvalidateFrom`, `ResetChildrenForRefill`, `DiscardLineBoxesFrom`, #355's pass re-entry and #371's rewind. Undoing an attempt on a *resumed* pass is a three-way question — below the resume point, at it, and above it — and "resumed" is not one state. Retracting lines is not retracting geometry: the words have to go too (`AwaitsTheNextFragmentainer`, which is self-healing). **The invariant that catches both directions of getting any of this wrong is one line over the fragment tree: every word claimed exactly once.**
