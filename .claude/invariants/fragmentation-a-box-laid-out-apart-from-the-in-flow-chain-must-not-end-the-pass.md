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

**A float that fits on a page is moved whole instead** (`CssBox.MoveWholeOntoTheNextPageIfItFits`). That is
safe only because:
- the float is placed before the in-flow content after it;
- `CssLayoutEngine.FloatBox` keeps a later float from rising above it (CSS 2.1 §9.5.1 rule 5). Without that
  clamp, the later float's static position was still on the page before, and it was placed there, above the
  float it follows. A float moved from inside an earlier block holds later floats in the same formatting
  context too (`HtmlContainerInt.MovedFloats`).

How the move is decided and placed:
- It is decided on the float's static position, since a relative offset takes no part in layout.
- It measures against each page's usable band, net of a `float: top` strip, a footnote area and a bottom
  page float. A review found the bare page top put a moved float on a `float: top` figure.
- It re-places the float at the new band top, which is where an `inside`/`outside` float learns its new
  side.
- It translates the float's subtree, which a plain inline box's `Location` does not survive: the inline flow
  never places that `Location`, so it resets it per layout. Without the reset, the move added itself to it
  again on every layout of the same tree.

A new path that lays an absolutely positioned box, a float, or anything else out of tree-order position must
do the same, or keep a scroll container around it monolithic.

**The standing exceptions.**
- A float or absolutely positioned box that is or holds a multi-column container keeps the breaking path,
  because its columns engine needs the attached fragmentainer (`CssBox.IsOrHoldsAMultiColumnContainer`).
  Reviews found `float: left; columns: 2`, and then an absolutely positioned `columns: 2` box, each missed,
  and each lost its last lines.
- A float inside a column keeps the breaking path too, because a column does not continue a slice the way a
  page does. Laid out unbroken, its lines past the column's foot were drawn below the page band.

Both still break, and still have #1339's loss (see the accepted gap on tall floats).

The multi-column absolute box's break still ends the pass. The content after it cannot simply be put at its
§9.3.1 position, because that page is already emitted:
- re-opening the page draws a short block, but no pass paginates content laid out behind it;
- so a long following block was sliced across the page margin;
- and a following multi-column block lost its first page.

`DomUtils.GetPreviousSibling` therefore keeps `main`'s placement for this one case, and the content after
such a box goes below it. See #1377 and
[the accepted gap](../accepted-gaps/content-after-an-absolute-multi-column-box-is-placed-below-it.md), which
records the three attempts that failed.
