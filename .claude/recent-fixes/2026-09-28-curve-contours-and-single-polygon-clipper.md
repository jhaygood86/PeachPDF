# GraphicsPath.GetCurveContours and one shared rectangle polygon clipper

First step of the PeachDrawing geometry work (shapes, measurement, stroke outlines, boolean ops), stacked on
`peachdrawing-core-paragraph`.

- **`GraphicsPath.GetCurveContours()`** reads the base class's recorded segment list with its curves intact
  (`CurveContour` / `PathCommand`). Everything curve-aware that follows (measurement, text on a path, curve-preserving
  boolean ops) needs it; `Flatten` alone would force each of them to re-approximate. Named `PathCommand`, not
  `PathSegment`, because `PeachPDF.Svg.PathSegment` already exists.
- **`Geometry.PolygonClipper.ClipToRect`** replaces two private Sutherland-Hodgman copies (`PeachDrawing/` and
  `PeachPDF/PdfSharpCore/Drawing/`). The copies differed only in an early exit once a pass leaves fewer than three points;
  the shared one keeps the early exit. Both callers already discard contours under three points (PdfSharpCore explicitly,
  the raster path because a degenerate contour fills nothing), so output is unchanged; the full PeachPDF, PeachDrawing and
  Core suites pass unmodified apart from the one test double that called the deleted class.

**Deliberately not done here:** moving `Stroker`/`FlatPath`/`PolygonSet` into Core. `PolygonSet` depends on the internal
`Affine`, which the whole rasterizer uses, so the move drags the rasterizer's math along. Nothing before the stroke-outline
phase needs it; move it then, with `Affine`.

Evidence: solution rebuild 0 warnings; PeachPDF.Tests 14,414 passed, PeachDrawing.Tests 151, PeachDrawing.Core.Tests 82
(net8.0, Debug). Release run and diff coverage still to do before the PR.
