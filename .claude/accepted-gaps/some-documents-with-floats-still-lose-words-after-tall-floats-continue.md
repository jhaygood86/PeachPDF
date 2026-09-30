# Some documents with floats still lose words, and a few lose words the previous behaviour kept

A float that runs across pages continues with the text beside it, which recovers most of what was lost, but two
kinds of residual are known and deliberately left:

- **Documents that were already losing words** still lose some, mostly floats that overlap each other (see
  [the second right float gap](a-second-right-float-that-does-not-fit-beside-the-first-overlaps-it.md)) and
  layouts the line-placement policy drops a first word from (see
  [the border box gap](text-beside-a-float-is-measured-against-its-border-box-and-a-negative-margin-float-can-hang.md)).
- **A small share of documents that rendered completely before lose a few words.** They are layouts that combine a
  multi-page float with several other features at once (tables, columns, absolutely positioned boxes, several
  floats). Delta-debugging three of them gave reductions that were still dozens of elements, and every reduction
  that did give a small document turned out to be one of the mechanisms listed in
  [the recent fix](../recent-fixes/2026-09-29-tall-floats-continue-with-their-text.md), each fixed in a different
  place. Closing the rest means changing how the container owns a fragmented float, which touches the fragmentation
  core; it is not a float-placement change and was not attempted here.

A duplicated word is a lesser failure than a lost one, and this change trades in that direction: on the ordinary
corpus words lost fall by an order of magnitude and duplicated words stay at zero. On the corpora that already
duplicated words, duplicates rise slightly (about 3%) while losses fall.

Tracking issue: #1523.
