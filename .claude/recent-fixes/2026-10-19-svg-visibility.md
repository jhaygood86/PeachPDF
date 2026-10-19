# SVG visibility

#1797. `visibility` is resolved in `SvgTreeBuilder.ApplyCommon` (inherited through `InheritedPaint.Visible`, onto `SvgElement.Visible`); the renderer skips painting in `PaintShape`, `RenderImage`, `RenderForeignObject`, text glyphs (per run, so a `visible` tspan inside hidden text paints), text decorations and `<textPath>`.

## Traps
- Only painting is suppressed. Clip paths are built by `BuildClipPath` (not `PaintShape`) and bounding boxes by `SvgGeometryBounds`, so a hidden shape still clips and sizes, as SVG 2 requires. Text is laid out in full and the hidden run's glyphs are skipped in `PaintGlyphs`; do not drop them from the glyph list (that would move their neighbours).
- `GlyphInfo.Omitted` is not usable for this: wrapped text removes omitted glyphs after layout.

## Not done
Markers of a hidden shape are not drawn (they are painted from `PaintShape`); SVG 2 lets a marker's content follow its own `visibility`.
