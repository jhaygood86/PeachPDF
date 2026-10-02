# SVG text CSS parity, round two (#1572)

What landed: `font`/`font-variant`/`text-decoration` shorthand expansion, `font-family` lists with per-character fallback,
decoration thickness/offset/position/`double`/skip-ink, `text-shadow`, `paint-order` (text), `xml:space`/`white-space`/`tab-size`,
`dominant-baseline`/`alignment-baseline`/`baseline-shift`, `textLength` (spacing). Leftovers: `.claude/accepted-gaps/svg-text-remaining-css-text-properties.md`.

Load-bearing ideas and traps:
- **Shorthands** are expanded once in `ResolveStyledAttr` through the CSS-OM (`SvgTextShorthands`), so every longhand benefits. The
  matched-stylesheet tier was already expanded by the CSS-OM; only `style=""` and presentation attributes needed it. `text-decoration`
  had to learn `text-decoration-thickness` in both `TextDecorationProperty` and the `PropertyFactory` longhand list — adding only the
  converter parses but silently drops the value.
- **Font fallback**: `GetFontFor` must go through `FontFamilyResolver` — handing the comma list to `RenderContext.GetFont` resolves
  the wrong family. Fallback fonts are canonicalised per `FaceKey` inside the run's closure because the resolver returns a fresh `Font`
  instance per character, and `PaintGlyphs`/`ResolveComplexScriptRuns` compare fonts by reference to batch. A font change breaks a batch.
  Three existing SVG tests assumed one font per run; they now assert the concatenated text.
- **Decoration** reuses HTML's `StrokeDecorationSegment`, `DecorationSegments` and `GetInkCrossings` (made `internal`); `auto` keeps the old
  underline position and 1-unit thickness exactly. Skip-ink is measured per glyph (a complex-script shaping run as one unit).
- **Shadows** paint from `PaintTextGlyphs` (the one choke point); a rotated glyph's shadow is pushed as `glyphTransform * translate(offset)`
  so the offset stays in user space. `MeasureTextBounds` grows by offset + 1.5 × blur so opacity-group tiles do not clip them.
- **Baselines** offset only `GlyphInfo.Py` (not the pen), so everything downstream (decoration, bounds, draw origin) follows.
  `baseline-shift` is cumulative through `ParentRun`; `ComputeFontContext` ignores it on non-text elements.
- **textLength** adds the gap to `Advance` and marks glyphs `SpacingAdjusted` so `PaintGlyphs` does not batch them back together.
Evidence: new test classes under `PeachPDF.Tests/Svg/` (`SvgTextShorthandTests`, `SvgTextFontFallbackTests`, `SvgTextDecorationGeometryTests`,
`SvgTextShadowTests`, `SvgTextPaintOrderTests`, `SvgTextWhitespaceTests`, `SvgTextBaselineTests`, `SvgTextLengthTests`), showcase
`svg_text_css_parity`, full net8.0 suite green apart from the known unrelated `AnonymousBoxDefaultingTests`.
