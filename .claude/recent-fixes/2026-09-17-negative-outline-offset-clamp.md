# Large negative outline offsets keep a paintable outside shape

`OutlineDrawHandler` previously applied `outline-offset + outline-width` directly to every side of the
border box. A sufficiently negative value inverted one or both dimensions of the resulting outer
rectangle, so `BoxEdgesDrawHandler` rejected it and painted no outline.

## The load-bearing rule

CSS Basic User Interface 4 requires the outside outline shape to remain at least twice the computed
outline width, with the constraint applied independently in each dimension. The renderer now clamps
the horizontal and vertical reach separately. It also accounts for sliced fragments where only one
edge in a dimension is adjustable, rather than assuming both sides always expand symmetrically.

The same axis-specific reach is used to derive rounded-corner radii, keeping the radius geometry tied
to the rectangle it describes.

## Evidence

`OutlineOffset_LargeNegative_KeepsOutsideShapeAtLeastTwiceTheOutlineWidth` covers both an offset that
would invert a square outline and a narrow rectangle where only the horizontal dimension needs
clamping. The outline showcase includes an extreme negative offset so the minimum visible shape can
also be inspected in the generated PDF.
