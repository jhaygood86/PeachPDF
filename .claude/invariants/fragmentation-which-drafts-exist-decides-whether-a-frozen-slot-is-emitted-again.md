# Which drafts exist decides whether a frozen slot is emitted again

_CSS Fragmentation Level 3. Tracker: [#320](https://github.com/jhaygood86/PeachPDF/issues/320)._

`FragmentEmitter._frozen` is populated by `BuildDraft`, one entry per box a draft was actually built for, and
it is the *only* gate on re-emission: `HtmlContainerInt.InvalidateEmittedFragmentsFor` returns early unless
`HoldsFragmentsFor(box)`. So **changing which drafts exist changes which already-frozen slots get emitted a
second time** — and a re-emitted slot is rebuilt from *final* geometry, not from the geometry it was frozen
at, so it can pick up line boxes that were laid out passes later and belong to a later fragmentainer.

Measured, and it is not a theoretical chain. A first attempt at [#446](https://github.com/jhaygood86/PeachPDF/issues/446)
replaced the emitter's word-membership test outright; `PageIndexOf`'s clamp then handed slot 0 every
not-yet-positioned word, taking the boxes frozen by slot 0's first emission from 100 to **404**. A later
reposition of three of those boxes now passed the gate, firing `InvalidateFrom(0)` three times where `main`
fires it none, so slots 0–4 were emitted again at `Finish()` — and slot 4 gained one line box (60 → 61
rectangles for one inline) that belongs to slot 5, drawn as a decoration strip at the bottom of the wrong
page. Nothing in the ~6,760-test suite caught it; the showcase rasterization diff did.

The rule for a change to any membership or filtering test in `BuildDraft`: prefer one that can only ever
*remove* a claim. A test that can add one has a second, invisible effect on the whole document's emission
order, and the evidence for it is the showcase pixel diff rather than the suite.
