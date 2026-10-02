# COLR over CFF, Porter-Duff composites, radial inner radius, linear p2 (#287)

- **CFF-flavoured COLR** needed only `OpenTypeFontface.IsColorFont` to stop requiring `glyf`: outlines already decode through
  `TryGetOutline` for CFF/CFF2, and `TypefaceExporter` embeds an `OTTO` font whole for the invisible-text layer. Fixture:
  `assets/fonts/ColorTestCff.otf` (`generate_color_cff_font.py`).
- **Porter-Duff** is done in `ColorGlyphPainter.PaintCompositeNode` with paint order plus `PushOutlineClip` to the other operand's
  glyph outlines (`CollectShapes`). SRC_OUT/DEST_OUT/XOR use `IColorGlyphTarget.PushOutlineComplementClip` (default interface member returning false => source-over): an even-odd clip of a rectangle plus the outline, one per shape so several shapes intersect to the complement of their union. The rectangle must cover the operands' shapes, not the (absent) enclosing clip: with `hasClip == false` a zero-size rect silently turned the clip into the shape itself (found by rasterizing). PLUS stays source-over (no additive blend in PDF).
  An operand with no glyph shape of its own (a bare gradient) is unclipped. Overlapping glyph shapes with translucency double-paint.
- **Radial**: concentric circles are exact by re-spacing stops as radii (`ExpandRadialStops`), inner radius = first color held,
  repeat/reflect tiled out to the clip's farthest corner. Different centers use the real two-circle gradient: `RadialGradientBrush.FocusRadius`, `RadialColorGlyphPaint.FocalRadius` and a `GetRadialGradientBrush` overload with `focalRadius`; PDF writes it as the shading's r0, and `PaintSource.RadialPaint` solves the conical quadratic (largest t with non-negative radius). Where the outer circle covers the focal circle, the later circle wins, so the focal color is not necessarily visible.
- **Linear p2**: axis = p0->p1 minus its component along p0->p2. Equals p0->p1 for the perpendicular case.
- Verified by rasterizing the fixture with PDFium and MuPDF (identical); suite passes (one unrelated memory-ratio test is flaky).
