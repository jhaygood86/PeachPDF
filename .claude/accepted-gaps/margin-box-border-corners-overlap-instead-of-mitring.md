# A page/margin box's border corners overlap instead of mitring — issue #1258

**Tracked bug, not a limitation argued through and accepted.** Pre-existing; made conspicuous by
#1237 and deliberately left out of it.

`MarginBoxRenderer.PaintBorder` paints four full-span edge rects (top/bottom span the whole
border-box width, left/right its whole height) in the order top, bottom, left, right — so each corner
square is painted twice and the left or right edge wins it outright, where `BoxEdgesDrawHandler` cuts
the corner along the outer-to-inner diagonal.

Measured, `@top-center { border: 20px inset rgb(128,128,128) }`: the top-right corner comes out
entirely `#d4d4d4` and the bottom-left entirely `#2c2c2c`, where each should be split on the diagonal.
The other two corners are right only because the two edges meeting there agree.

**Older and more general than the bevel**: `border-top: 4pt solid red; border-right: 4pt solid blue`
on a margin box already paints a fully blue top-right corner. #1237 only made it reachable from a
single `border: inset` declaration, by giving the four edges different faces for the first time —
`groove`/`ridge` showed it before that, their edges already being two-toned.

`MarginBoxRendererBorderTests.AllFourEdges_PaintTheirOwnIndependentlyResolvedWidthAndColor` **pins
the full-span rects**, so closing this means rewriting that test to expect four mitred quads — it is
not a test that will simply keep passing.

Do not read `PaintBackgroundAndBorder`'s doc comment as having settled this: its "a margin box never
fragments across pages, so … it has no shared corner with a neighboring fragment to mitre into" is
about fragment-to-fragment continuity and says nothing about the box's own four corners.

`docs/html-css-support.md` **does** describe this to readers, in the bevelled-border section's
closing sentence about a page/margin box ("its edges are painted full-width and full-height rather
than mitred..."). That note and this file are deleted together when #1258 closes.
