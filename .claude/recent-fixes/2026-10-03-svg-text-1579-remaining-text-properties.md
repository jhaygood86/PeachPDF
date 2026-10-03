# SVG text: #1579 remaining text properties

Landed: `textLength` `spacingAndGlyphs` (per-glyph stretch transform; scales compose for nested elements), `textLength` on `<textPath>` and vertical text,
shadows on transformed glyphs (blur layer sized to the transformed box), `font-size-adjust` (HTML + SVG, per-face ratio, `em` stays unadjusted),
`font-synthesis*`, BASE-table baselines, `-webkit-text-stroke`, `text-rendering`, `font-language-override` (`ShapeSettings.LanguageSystemTag`, GSUB only),
and SVG 2 auto-wrapped text (`SvgRenderer.WrappedText.cs`, `SvgTextShapes.cs`).

- `font-synthesis` is a pair of `PaintFontStyle` flags (`NoSyntheticBold`/`NoSyntheticItalic`), not a `GetFont` parameter: the style bits are already in every
  font cache key and abstract `CreateFontInt` signatures stay untouched for third-party `RenderContext`s. Flags are only set when that style was actually
  requested, so `font-synthesis: none` on regular text does not split the font cache.
- `font-synthesis` shorthand needs its own value type: the generic shorthand export would reset omitted longhands to `auto` instead of `none`.
- `ResolveStyledAttr` must not call `GetAttribute` for `-webkit-*` names (an XML attribute name cannot start with `-`).
- Evidence: full Release suites pass (PeachPDF.Tests 15283, Core/Drawing/Text/SourceGenerators suites), zero-warning Release Rebuild; wrapping showcase rasterized in PDFium and MuPDF.
- Residuals: see the accepted-gap file `svg-text-remaining-css-text-properties.md`.
