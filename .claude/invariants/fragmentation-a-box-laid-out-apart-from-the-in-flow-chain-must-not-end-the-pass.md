# A box laid out apart from the in-flow chain must not end the pass

A layout pass fills one fragmentainer and ends where a break token says the next one resumes. The token
names one point in tree order, so everything after that point is laid out on the next pass, on the next
page. That is only right for in-flow content, whose tree order is its block order.

A float or an absolutely positioned box is placed apart from that order: a float beside the content that
follows it, an absolutely positioned box by its offsets, with the in-flow content after it starting where
it would without it (CSS 2.1 §9.3.1). If a break is taken *inside* such a box, the pass ends there and the
next pass resumes inside it on the following page, while the in-flow content after it in tree order is
placed back beside or above it, on the page the break left. That page is already emitted, so the content
is drawn on no page.

Measured symptoms, each on a 300×200pt page with 12pt lines:

- a block beside a 30-line float drew only its last four lines (#1339);
- all ten paragraphs after a 30-line absolutely positioned box were lost;
- text beside a float inside an auto-height `overflow: hidden` wrapper was lost once the wrapper could
  break (review of #1334).

So such a box is laid out unbroken, with the fragmentainer detached and word page breaks suppressed:
`CssBox.LayoutBlockChildUnbroken` from the block frame, `CssLayoutEngine.LayoutContentUnbroken` from the
inline flow. It then shows one slice per page. A float that fits on a page is moved whole instead
(`CssBox.MoveWholeOntoTheNextPageIfItFits`), which is safe only because the float is placed before the
in-flow content after it, and because `CssLayoutEngine.FloatBox` keeps a later float from rising above it
(CSS 2.1 §9.5.1 rule 5). Without that clamp the later float's static position was still on the page before,
and it was placed there, above the float it follows. The move is decided on the float's static position
(a relative offset takes no part in layout) and re-places the float at the new page top, which is where an
`inside`/`outside` float learns its new side.

A new path that lays a float, an absolutely positioned box, or anything else out of tree-order position
must do the same, or keep the scroll container around it monolithic. The one standing exception is a float
holding a multi-column container, whose columns engine needs the attached fragmentainer, and a float
inside a column, which a column does not continue the way a page does: laid out unbroken, its lines past the
column's foot were drawn below the page band. Both still break and still have #1339's loss (see the accepted
gap on tall floats).
