# A form widget under `perspective` or in a `preserve-3d` context is the upright bounding box

A link or form widget over an element drawn through a projective map (a `perspective` element, or a plane of a 3D
rendering context) is placed where it was drawn: `/Rect` is the bounding box of the projected shape. A link also writes
the projected quadrilateral as `/QuadPoints`, so it is exact. A widget has no such entry, and its appearance stream can
only be carried onto the box by `/Matrix`, which is affine; so under a non-affine map the appearance is built upright at
the control's own size and the reader stretches it over the bounding box, while the painted chrome beside it is warped.
The click target is right; the widget's own look, once a reader draws it, is not the warped picture.

An affine transform (rotation, scale, skew, a plane brought nearer under `perspective`) is exact: the map's linear part is
the appearance's `/Matrix` (`AnnotationPlacement`).

A widget's `/Matrix` is the static appearance only: a viewer that regenerates the appearance (the user edits the field, or
the viewer honours `NeedAppearances`) rebuilds it from `/DA` and `/Rect` without the baked matrix and draws the control
upright over the bounding box, as for a non-affine map. No `/MK /R` is written, which could only express quarter turns.

`/QuadPoints` is written in the order Acrobat reads, top-left, top-right, bottom-left, bottom-right. ISO 32000 describes
the quadrilateral as counterclockwise, and a reader that hit-tests by winding would read this order as a bow tie; readers
in practice take a link's `/Rect`, so the box stays the click target either way.

Why it is left: PDF gives a widget no quadrilateral, so there is nothing faithful to write. Approximating the warp by its
linearisation at the centre (as the invisible text layer does) would draw a skewed widget that matches neither the picture
nor the click target at the edges.

Also left, by the same hand: `/QuadPoints` is written only when the file declares PDF 1.6 or later (so not under a 1.4
header, the PDF/A-1 case), where the link is the bounding box alone; and a link whose bounding box is wholly off
the page is not written.

Tracked as [issue #1711](https://github.com/jhaygood86/PeachPDF/issues/1711), a follow-up to [issue #1708](https://github.com/jhaygood86/PeachPDF/issues/1708).
