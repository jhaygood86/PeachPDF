# A block-level float taller than a page continues with the text beside it

**Load-bearing idea.** A block-level float is laid out as its own independent fragmentainer pass
(`HtmlContainerInt.CreateIndependentPageFragmentainer`), entered through `EnterNestedFragmentainer` from
`CssBox.LayoutBlockChild`, and resumed with `ResumeAt` for as long as it reports a pending break token. Each pass's
slot goes to the `FragmentEmitter` like any other box's, so the float has a fragment on every page it reaches and
the paint phase draws it from the fragment tree with no float-specific code. The text beside it is not special
either: the float's coordinates stay in the container's `CssFloatCoordinates`, and the lines of the following block
are queried against them on each page as before. Before this, the float was one of its parent's own block children,
so the parent's pass stopped at the float's break token and the siblings after it were only laid out once the float
had ended: the text beside a tall float was dropped.

**What it does not fix, and the honest size of it.** Over 2,500 generated layouts this recovers most of the lost
words and leaves some behind (numbers in the pull request). It cannot be made regression-free by a local change: the
independent pass changes which fragmentainer the enclosing pass is filling while the float runs, and every mechanism
that assumes the parent's pass is the only thing emitting into its own slots needed a guard. Several were found by
running corpora, each a real loss of words on documents the base rendered completely, and each needed its own fix in
a different place (below). Closing the rest means reworking how a container owns a fragmented float, which is a change
to the fragmentation core, not to float placement.

**Traps found by running it (each has a test).**

- *Break propagation.* A cleared block that lands on a later page makes its parent record a break before its first
  in-flow child, and css-break-3 §3.1 propagation turned that into a break before the parent, because the float
  ahead of it is out of flow. The parent then restarted from the later page and re-laid the float there, starting it
  at the top of that page. `EarlyBreak.NamesAPropagatingBreakBefore` now declines when a preceding sibling is a float
  that ran as several passes (`CssBox.FragmentedAcrossFloatPasses`).
- *Clearance past the pass.* A box that clears the float is placed by clearance in a later fragmentainer than the one
  the enclosing pass is filling. Laying it out there broke its first line with an inline token, and resuming that put
  the line at the top of the band, beside the float, instead of at the clearance. `PlaceAndSizeBlockChild` now states
  the break before the box, at the clearance, for a cleared box that follows such a float.
- *Relocation by `break-inside: avoid`.* The mover relocated a wrapper holding a multi-pass float; what the float
  placed on earlier pages is recorded by its break tokens, not by where it sits, so the move dropped it (38 of 42
  words in the reduced case). The avoid is relaxed for a box holding such a float, as §4.3 allows for content that
  cannot be satisfied.
- *Stale emitted page.* A sibling that follows a fragmented float can first be placed beside the float's top, on a
  page that has already been emitted. It has no fragments yet, so `InvalidateFrom` on the box itself finds nothing;
  `InvalidateEmittedFragmentsForPlacement` runs after `CommitBlockChildOffset` and reopens the page it lands on. Measured both ways: with it the mixed-feature corpus loses 140,968 words, without it 143,265, and the dense-float corpus 42,791 against 47,297; the price is that some layouts then draw a few words twice (a hand-built document of floats on both sides around a list draws 173 or 176 words where it has 162). A duplicated word was judged the lesser failure. It has no reduced unit test (a document that separates the two builds only did so with the real Arial metrics, and the reductions stayed large), so the corpus numbers are its evidence.
- *A continuation returning a token it returned before* would spin forever; the loop keeps the set of tokens seen
  (and a hard cap) and on a repeat lays the float out unbroken, the previous behaviour.
- *Inline floats are left alone.* Laying an inline float's content out at its assigned position when the container
  is fragmenting recovered 627 more words on the inline corpus but made three documents lose words the base kept, so
  that branch is not part of this change.

**Measurement traps.**

- Clipped text extracts as a prefix, so with words like `w1` and `w12` a clipped word looks like a duplicate of
  another; the corpora use prefix-free unique words.
- Word counts alone hid the clearance defect completely: the words were all present, one of them in the wrong place.
  Comparing word positions for every document the base renders completely is what found it.

**Not done, and why.** Measuring lines against the float's margin box, dropping a float below an earlier float's
margin box, and the negative-margin hang change placement for documents with no tall float, and the version that
fixed them dropped words the current line policy keeps. See
[the accepted gap](../accepted-gaps/text-beside-a-float-is-measured-against-its-border-box-and-a-negative-margin-float-can-hang.md)
and [the residual losses](../accepted-gaps/some-documents-with-floats-still-lose-words-after-tall-floats-continue.md).
