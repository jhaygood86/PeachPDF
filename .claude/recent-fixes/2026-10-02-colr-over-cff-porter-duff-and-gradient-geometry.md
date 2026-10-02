# COLR over CFF, Porter-Duff composites, radial inner radius, linear p2 (#287)

- **CFF-flavoured COLR** needed only `OpenTypeFontface.IsColorFont` to stop requiring `glyf`: outlines already decode through
  `TryGetOutline` for CFF/CFF2, and `TypefaceExporter` embeds an `OTTO` font whole for the invisible-text layer. Fixture:
  `assets/fonts/ColorTestCff.otf` (`generate_color_cff_font.py`).
- **Porter-Duff** is done in `ColorGlyphPainter.PaintCompositeNode` with paint order plus `PushOutlineClip` to the other operand's
  glyph outlines (`CollectShapes`). Not expressible, so still source-over: SRC_OUT, DEST_OUT, XOR (need a complement clip), PLUS.
  An operand with no glyph shape of its own (a bare gradient) is unclipped. Overlapping glyph shapes with translucency double-paint.
- **Radial**: concentric circles are exact by re-spacing stops as radii (`ExpandRadialStops`), inner radius = first color held,
  repeat/reflect tiled out to the clip's farthest corner. Different centers keep the focal-point approximation. No public API change.
- **Linear p2**: axis = p0->p1 minus its component along p0->p2. Equals p0->p1 for the perpendicular case.
- Verified by rasterizing the fixture with PDFium and MuPDF (identical); suite passes (one unrelated memory-ratio test is flaky).
