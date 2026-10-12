# A link annotation or form widget over a transformed element stays at the untransformed rectangle

`<div style="transform: rotate(30deg)"><a href="...">link</a></div>` paints the link rotated, but its `/Link` annotation
(and an `<input>`'s widget) is placed at the rectangle layout gave it, untransformed: `HtmlContainerInt.GetLinks` and
`PdfGenerator.HandleFormFields` read `box.Rectangles`/`box.Bounds` straight off the box tree, which are in layout
coordinates, and neither applies the cumulative `transform` (nor a perspective warp) of the box's ancestors.

What *is* handled, and is not part of this gap: an element whose transform leaves no visible area (non-invertible 2D
matrix or singular 4x4, or an edge-on plane) writes no annotation and no widget at all - see
[the fix](../recent-fixes/2026-10-11-collapsed-transform-takes-links-and-3d-context-with-it.md).

Why it is left: a correct fix maps the rectangle through the cumulative transform of every ancestor (including
`transform-origin`, which is resolved against each box's own size, and the 3D/perspective path, where the image is a
quadrilateral and a PDF `/Rect` can only be its bounding box unless the annotation also carries `/QuadPoints`), and the
form-widget side additionally has the AcroForm appearance stream to keep consistent. That is a feature of its own, not a
guard.

Tracked as [issue #1708](https://github.com/jhaygood86/PeachPDF/issues/1708), split out of [issue #1703](https://github.com/jhaygood86/PeachPDF/issues/1703).
