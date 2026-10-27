# Win2D: DirectWrite text mode has no outlines, no index-addressed glyphs of its own, and does not lay out like the other backends

`Win2dTextEngine.DirectWrite` shapes, measures and draws with `CanvasTextLayout`, so it looks like the rest of Windows, at these costs:

- **Layout differs.** DirectWrite's metrics, hinting, fallback and kerning are not `PeachDrawing.Text`'s, so a document laid out with
  DirectWrite fonts does not break lines or measure identically to the PDF, SVG or raster canvases. This is the point of the mode, and why
  `PeachDrawing` stays the default.
- **No outlines.** Win2D hands back `CanvasGeometry` from a text layout but no way to enumerate its figures into a `GraphicsPath` without
  implementing `ICanvasPathReceiver` across the WinRT boundary, which is not worth it for a mode whose draw path is `DrawTextLayout`.
  `GetTextOutline` returns `null`, which `Canvas.GetTextOutline` documents as "fall back to `DrawString`"; text with a gradient fill or
  stroke therefore falls back to a solid colour, and `text-decoration-skip-ink` skips nothing.
- **`DrawGlyphs` needs a matched typeface.** Glyph indices belong to a font file. A DirectWrite font tries to match the same family in the
  `PeachDrawing.Text` font set and draws from that file's outlines; with no match the call draws nothing.
- **Font features are best effort.** `ShapeSettings.ExplicitFeatures`, `Kerning` and `Ligatures == None` map to `CanvasTypography`;
  `font-variation-settings` axes, `unicode-range` splitting and the CSS caps/numeric variants are not mapped.
- **Fonts a document adds** are written to a context-owned temp folder (Win2D loads a font from a file address, with the file's own family
  name after `#`) and deleted with the context.
