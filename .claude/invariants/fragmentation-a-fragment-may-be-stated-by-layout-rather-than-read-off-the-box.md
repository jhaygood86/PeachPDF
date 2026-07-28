# A fragment may be stated by layout rather than read off the box

_CSS Fragmentation Level 3, css-tables-3 §6.1. Tracker: [#320](https://github.com/jhaygood86/PeachPDF/issues/320)._

`FragmentEmitter.BuildDraft`'s gate is no longer "does this box have content here". It is **"does it have
content here, or did layout state a rectangle for it here"** — the second arm being a box whose fragment
holds none of its own content, which today is css-tables-3 §6.1's table cell that finished before its row
did (`RecordContinuationShell`, stated by `CssLayoutEngineTable.LayoutBodyRow`).

Two consequences a change in this area has to keep true.

**`BoundsOf` is no longer the answer for every draft.** A stated fragment's extent is the statement, not
the box — the box's own bounds describe the fragmentainer that *placed* it, which is a different one, and
a continuation may not be given a second `Location`
([why](fragmentation-a-continuation-may-not-move-geometry-an-earlier-fragmentainer-emitted.md)). `ExtentOf`
is the single place that resolves this (`draft.ShellRect ?? BoundsOf(...)`), and everything downstream —
`RectOf`, `LinesOf`, the §6.2 strip — reaches it through `ExtentOf`. New code that reads `BoundsOf`
directly to answer "where is this fragment" gets the wrong band for a stated one, silently: the fragment
still exists, it is simply drawn a page or more away.

**A statement continues a fragment; it must never invent one.** The gate honours a statement only for a
box `_frozen` already holds, which is what keeps
[which drafts exist decides whether a frozen slot is emitted again](fragmentation-which-drafts-exist-decides-whether-a-frozen-slot-is-emitted-again.md)
from firing: `_frozen` membership is unchanged, so no frozen slot is re-emitted that would not have been.
And a stated fragment never sets `hasPrintableContent` — CSS Paged Media Level 3 §3.2 excludes backgrounds
and borders from printable content by name — so it cannot turn an otherwise empty slot into a page.
Removing either guard widens the change from "a cell keeps its box" to "the emitter can materialize pages",
and the evidence for that is a showcase byte-diff rather than the suite.

**Which fragmentainer a statement belongs to is asked of the rectangle, never of the slot it was recorded
against.** `FragmentRegion.Contains` decides, as it does for words and lines; the slot survives only as
what `ClearContinuationShells` sweeps by, where a slot naming a band earlier than the rectangle's merely
clears more than it had to and the pass doing the clearing re-states. That tolerance was doing real work
when the row loop's band was a counter — a statement keyed to band *k* could carry a rectangle lying in
band *k+1*. The counter is now derived from where the cursor is
([#432](https://github.com/jhaygood86/PeachPDF/issues/432), and
[the trap that had to go first](fragmentation-a-stale-cursor-can-be-load-bearing-compensation-for-a-bad-estimate.md)),
so the two agree — but the rule stands, because what makes it right is that a rectangle knows which band
it is in and a recorded index only knows what its writer believed.
