# A box laid out apart from the in-flow chain must not end the pass

A layout pass fills one fragmentainer and ends where a break token says the next one resumes. The token
names one point in tree order, so everything after that point is laid out on the next pass, on the next
page. That is only right for in-flow content, whose tree order is its block order.

An absolutely positioned box is placed apart from that order: by its offsets, with the in-flow content after
it starting where it would without it (CSS 2.1 §9.3.1). A float is placed apart from it too, beside the
content that follows it.

If a break is taken *inside* such a box in the enclosing pass:
- the pass ends there;
- the next pass resumes inside the box on the following page;
- meanwhile the in-flow content after it in tree order is placed back beside or above it, on the page the
  break left;
- that page is already emitted, so the content is drawn on no page.

Measured symptom, on a 300×200pt page with 12pt lines: all ten paragraphs after a 30-line absolutely
positioned box were lost (#1349's review). A block beside a 30-line float drew only its last four lines
(#1339).

So such a box runs as **its own fragmentainer passes**, not as part of its parent's. `CssBox.LayoutBlockChild`
gives a block-level float and an absolutely positioned block child an independent fragmentainer for each
page the box reaches, resumed by the box's own break token, so its break never ends the pass of the block
around it. The box breaks between its lines and adds the pages it needs, as a browser prints it.

- A float starts in the slot being filled.
- An absolutely positioned box starts in the slot its offsets put its top in
  (`CssBox.AbsoluteTopBeforePlacement`), which is usually an earlier one than the pass reaching it in tree
  order, and one the emitter has already frozen. `HtmlContainerInt.InvalidateEmittedFragmentainersReceiving`
  re-opens the pages the box reaches, content included, once its position and height are final. Without it
  the box is drawn on no page.
- `position: fixed` is not affected: the emitter places a fixed box on every page itself.

A float among inline content is still laid out unbroken, by `CssLayoutEngine.LayoutContentUnbroken`, and a
box inside a multi-column container (a fragmentainer with its own band) keeps the breaking path, as it always
did.

**The trap this replaced.** An earlier version of this fix laid an absolutely positioned box out in one
piece, with the fragmentainer detached and word breaks suppressed, and let each page show the slice of it
that fell there. It kept the in-flow content, but a line straddling a page edge was cut, nothing inside was
relocated by §4.3, and words past the last page were lost. On 592 generated documents with absolutely
positioned boxes and no positioned ancestor it left 57 of the 313 documents that `main` rendered completely
losing words, against 21 with passes of its own, and lost 1,542 words in all against 1,036.

A new path that lays an absolutely positioned box, a float, or anything else out of its tree-order position
must do the same, or keep a scroll container around it monolithic.
