# `SvgCanvas` must read every coordinate the way `PdfCanvas` and `RasterCanvas` do

The third backend of the same contract as [[raster-graphics-mirrors-pdfcanvas-unit-conventions]], and it has the same trap with a
different shape. The conventions there say which `Canvas` coordinates are **layout units** (`DrawRectangle`, `DrawLine`,
`DrawPolygon`, `DrawImage`, glyph positions, `PushClip(Rect)`, brush geometry, a transform's translation) and which are **already
points** (every coordinate of a `GraphicsPath`, a pen's width and dash lengths). `PdfCanvas` and `RasterCanvas` keep points as
their user space and divide the layout units; `SvgCanvas` does the opposite - the document's user space *is* the layout unit, so
the `viewBox` is in layout units and a layout-unit coordinate is written as it arrives - and so it must **multiply** the point
group by `PixelsPerPoint` when it writes it (`SvgCanvas.PathData`, the pen width and dash lengths in `Stroke`). A `PushTransform`
matrix is written as given for the same reason (its translation is in layout units, its linear part has no unit).

**The symptom, measured:** at `PixelsPerInch = 96` (`PixelsPerPoint` 1.333) every box drawn from a path - rounded borders and
backgrounds, clips - landed at 0.75 of its place and size, while the text beside it, written in layout units, was right; at
`PixelsPerPoint` 1 nothing was wrong, which is why the unit tests and the SVG examples (all at 1) passed. It showed only in the
TestHarness's `--svg` output of `border_radius_96dpi`, 30 mean levels away from the PDF render in the two-renderer comparison
(resvg and Chromium agreed with each other, so it was the backend, not a viewer). `SvgShapeTests`'
`PathCoordinatesAndPenWidthsAreInPointsWhileEverythingElseIsInLayoutUnits` pins it at `PixelsPerPoint` 2.

A method added to `Canvas` needs an answer for which group each coordinate belongs to before `SvgCanvas` implements it, and a test
at a `PixelsPerPoint` other than 1.
