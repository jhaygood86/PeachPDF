# A pass that continues a box may not move geometry an earlier fragmentainer emitted

_CSS Fragmentation Level 3 §2, css-tables-3 §6.1. Tracker: [#320](https://github.com/jhaygood86/PeachPDF/issues/320)._

A box that spans fragmentainers keeps **one** `Location` and **one** set of `CssRect`s. The fragments
an earlier pass emitted were built from those, and `FragmentEmitter` freezes them at the end of that
pass. So any code on a resumed pass that offsets a box's subtree — `CssBox.OffsetTop` and everything
that calls it — moves content the earlier fragmentainer has already claimed, and the two fragments
then disagree about where it is.

The measured symptom is a **line drawn on two pages**, not content vanishing, which is why it survives
a "nothing was dropped" check. `CssLayoutEngine.ApplyCellVerticalAlignment` ran on a `<td>` that
resumed from a carried record and then finished: it is in neither the stopped list nor
`TableBreakToken.FinishedCells`, so nothing excluded it. `dist` came out large and positive — the
cell's `ClientBottom` names the continuation's row, while `GetMaximumBottom` walks live boxes whose
kept-in-place child still reports the page it began on — and the `<p>` inside moved from Y 22.7 to
235.3 between the two passes. That put one line across the 290pt boundary, and both bands claimed its
14 words.

**A box's own `Location` is the same rule, and it has one exception.** `CssLayoutEngineTable`'s row
loop wrote each pass's row top into every cell it placed, including one continuing from an earlier
fragmentainer — and the emitter, notified the box moved, then rebuilt that fragmentainer from where the
box is *now* and found nothing of it there: the whole table vanished from the page it began on. On the
page grid a continuation therefore keeps the `Location` its first fragment was built from. **Inside a
fragmentainer with a band of its own — a multi-column column — it does move**, exactly as
`CssBox.ResumeInTheNextFragmentainer` does, because columns differ in precisely the axis the page grid
holds constant; stating the rule without that exception lost half the content of a table nested in a
multi-column container. Any new "keep it where it was" guard has to ask which kind of fragmentainer it
is in.

The rule this generalizes to, and the one to apply before adding any new offset: **only a fragment
that both opens and closes in the fragmentainer being filled has room of its own to distribute.** A
box that continues elsewhere overfills its fragment by definition; a box that continues an *earlier*
fragment had its content position settled by the pass that opened it. `TableRowCursor` answers both
halves — `FinishedOnAnEarlierPass`, `ResumedFromAnEarlierPass` — and `LayoutBodyRow`'s alignment loop
asks all three questions before it aligns anything.

The sibling rule about what a box's geometry *means* while it carries a record is
[a box's own measurements are only valid at specific times](fragmentation-a-boxs-own-measurements-are-only-valid-at-specific-times.md);
this one is about writing rather than reading.
