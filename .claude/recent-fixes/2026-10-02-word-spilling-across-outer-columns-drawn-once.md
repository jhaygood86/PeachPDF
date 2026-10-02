# A word spilling across two outer columns was drawn by both (seed 252)

A multi-column container nested in an outer column is filled once per outer column it lands in, and each fill records its
own inner columns. A word wider than its (narrow) inner column spills past the column's edge, so the rectangle it
occupies overlaps a region of the other outer column's fill too. `FragmentEmitter.StartsInAnotherInstance` is what hands
such a word to the instance it starts in, but it only looked at instances with the same band top, which is true of the
columns of one fill and never of the same container's fills under two outer columns (their bands differ). Both fills
drew the word, at the same position.

Fix: match another instance whose band the word lies in (vertical overlap) instead of one with an equal top.

Seed 252 itself changes in no measurable way: its remaining 14 "lost" words are words the inner columns place past the
page's right content edge, which the visibility check counts as outside the page, not lost layout. The corpus
(300 documents) is identical to main; the reduction (`word` text, a spacer, then the nested containers with a trailing
empty `<p>`) went from 3 doubled words to none.