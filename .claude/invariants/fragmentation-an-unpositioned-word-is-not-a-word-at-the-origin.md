# An unpositioned word is not a word at the origin

_CSS Fragmentation Level 3. Tracker: [#320](https://github.com/jhaygood86/PeachPDF/issues/320)._

A `CssRect`'s position is a `double` pair that starts at 0 and is written when the flow reaches the
word. Nothing in the type distinguishes *placed at document Y 0* from *never placed*, and the two
have to be told apart, because document Y 0 lies inside the **first** page slot's own band — so the
first page's fragment claims an unplaced word, paints it, and puts it in that page's text layer
([#433](https://github.com/jhaygood86/PeachPDF/issues/433): 2,999 of 3,000 words claimed by a page
showing 1,153).

**`CssRect.AwaitsTheNextFragmentainer` is how the difference is stated**, and it is self-healing:
`CssRect.Top`'s setter clears it, so being positioned is what makes a word this layout's. The
corollary is the rule to keep: **any mechanism that can leave a word unreached has to say so before
the attempt starts**, because afterwards there is nothing to read — the position the word carries is
indistinguishable from a real one. Three callers say it (`CssBox.ResetForRefill`,
`CssBox.DiscardLineBoxesFrom`, and the block's own inline flow in
`CssLayoutEngine.CreateLineBoxes`), and all three say it the same way: mark, then let placement
clear it.

**The position an unreached word carries is not always 0**, which is what makes "just check for the
origin" the wrong fix. A box laid out a second time — the reflow loop settling per-page widths, a
§4.3 mover laying a subtree out again — leaves the previous attempt's coordinates on every word the
new attempt does not reach, and those are ordinary-looking coordinates in the middle of the
document. The showcase symptom for #433 was exactly that: a line of the *next* page's text painted
at the foot of `paged_media_horizontal_reflow`'s pages 2 and 4, at a stale X as well as a stale Y.

**Marking may only be done by the pass that opens the flow.** On a resumed pass the words below the
resume point belong to a fragmentainer already filled and frozen; marking them takes back content
another page legitimately holds (measured: 9 test failures when the `resume is null` guard is
removed from `CreateLineBoxes`).
