# A box laid out apart from the in-flow chain must not end the pass

A layout pass fills one fragmentainer and ends where a break token says the next one resumes. The token
names one point in tree order, so everything after that point is laid out on the next pass, on the next
page. That is only right for in-flow content, whose tree order is its block order.

An absolutely positioned box is placed apart from that order: by its offsets, with the in-flow content after
it starting where it would without it (CSS 2.1 §9.3.1). A float is placed apart from it too, beside the
content that follows it.

If a break is taken *inside* such a box:
- the pass ends there;
- the next pass resumes inside the box on the following page;
- meanwhile the in-flow content after it in tree order is placed back beside or above it, on the page the
  break left;
- that page is already emitted, so the content is drawn on no page.

Measured symptoms, each on a 300×200pt page with 12pt lines:
- all ten paragraphs after a 30-line absolutely positioned box were lost (#1349's review);
- a block beside a 30-line float drew only its last four lines (#1339).

So such a box is laid out unbroken, with the fragmentainer detached and word page breaks suppressed:
- from the block frame, by `CssBox.LayoutBlockChildUnbroken`;
- from the inline flow, by `CssLayoutEngine.LayoutContentUnbroken`.

It then shows one slice per page.

A new path that lays an absolutely positioned box, a float, or anything else out of tree-order position must
do the same, or keep a scroll container around it monolithic.

**The standing exception.** A box that is or holds a multi-column container keeps the breaking path, because
its columns engine needs the attached fragmentainer (`CssBox.IsOrHoldsAMultiColumnContainer`). A review found
an absolutely positioned `columns: 2` box missed, and it lost its last lines. Such a box's break still ends
the pass, and the content after it cannot simply be put at its §9.3.1 position, because that page is already
emitted:
- re-opening the page draws a short block, but no pass paginates content laid out behind it;
- so a long following block was sliced across the page margin;
- and a following multi-column block lost its first page.

`DomUtils.GetPreviousSibling` therefore keeps `main`'s placement for this one case, and the content after
such a box goes below it. See #1377 and
[the accepted gap](../accepted-gaps/content-after-an-absolute-multi-column-box-is-placed-below-it.md), which
records the three attempts that failed.
