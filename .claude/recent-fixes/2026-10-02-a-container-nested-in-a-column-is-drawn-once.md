# A multi-column container nested in another one's column is drawn once

`<div style="columns:3"><div style="columns:2">32 words of bare text and a paragraph</div></div>` drew every word twice, at the
same position. 118 and 76 of the generated corpus reduced to it; across the corpus it was most of the remaining drawn-twice
words (140 on 300 documents).

**Three separate causes, found by logging what the emitter saw (not by reading it).**

1. *The live fallback.* The fragment walk reads a child "no fragmentainer holds" live, over the page's whole region: right for
   an out-of-flow child, which css-multicol resolves against the container. A container nested in an outer column is walked
   once per outer column, and an in-flow child that the inner columns of *this* outer column do not hold is in another outer
   column's, so it was drawn again at its final position. `ChildrenOf` now skips an in-flow child there
   (`capture is not null`); an out-of-flow one is still read live.
2. *A resumed container cleared its earlier columns.* The columns engine clears what a container recorded in its slot when it
   is laid out again. A container resumed in the next outer column of the same page is in the same slot, so the clear took the
   instances it had recorded under the first outer column with it, and that column read the live geometry instead. The
   emitter's instances are kept for a container resumed inside a column (`keepEmitterInstances`); the retry's own cleanup is
   index based, so `recordedSoFar` starts at the preserved count.
3. *Neighbours across parents.* `RecordCapturedInstance` took the previous instance in the list as the column to the left,
   whatever its parent context. Once instances of different outer columns share a list (cause 2), the first inner column of
   the second outer column treated the last of the first as its neighbour and handed it words it should keep: seed 43 lost a
   word. The left neighbour is now the previous instance with the same parent context.

A fourth rule closes the case that remained: a word wider than its column (inner columns of 22pt) spills into the next
outer column's region and both drew it. `StartsInAnotherInstance`: a word that does not start inside an instance's region
yields to an instance of the same container, in the same row, that holds it at a position inside its own region.

**What did not work.** Each change alone was measured. Skipping the clear without the neighbour fix made 18 documents lose
words; the fallback fix alone removed about half of the doubled words and exposed nothing; neither is enough.

**Not done / left.** Seed 112 loses one word it drew before: a float wider than its column inside the nested container,
which was only visible because the container was drawn twice (the float-wider-than-its-column work for nested containers).
It is 12 fewer drawn-twice words against 1 more lost.

Evidence: `NestedMulticolContentTests` (two of three fail without the change; the third guards the multi-page loss); full
net8.0 suite passes; 300-document corpus against main: 9 documents draw fewer words twice (140 to 28), none draw more,
one loses one more word (above).
