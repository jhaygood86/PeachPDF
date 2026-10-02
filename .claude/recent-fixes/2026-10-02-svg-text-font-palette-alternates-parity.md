# SVG text: font-palette, font-variant-alternates and friends (#1293)

Load-bearing idea: no new shaping/painting machinery. `SvgTreeBuilder` gained the two per-document
registries (`@font-palette-values`, `@font-feature-values`) as optional `Build` parameters and the
existing HTML resolvers (`FontVariantAlternatesResolver`, `FontPaletteResolver`,
`FontVariationSettingsResolver`, `FontWeightResolver`, `FontObliqueAngleResolver`) are called from
`ComputeFontContext`/`BuildTextRunCore`. `DerivedStyle.MergeExplicitFeatures` became `internal` so the
alternates-beat-`font-feature-settings` rule (CSS Fonts 4 §6.8) is shared, not copied.

Traps:
- The raw cascaded strings travel in `FontContext`; resolution needs the used `Font` (palette count) and
  family, so it happens in `BuildTextRunCore` after the font is realized.
- Registries come from three places: `CssBoxSvg.EnsureDocument` (page registry), `ImageLoadHandler.LoadSvgFromStream`
  and `SvgTreeBuilder.BuildNestedSvgDocument` (the SVG's own `<style>` CssData). Standalone SVG does NOT register its own
  `@font-face` (unchanged, pre-existing).
- `font-style: oblique 12deg` used to fail the `== "oblique"` check and render upright; now `StartsWith`.
- `GetFont` now always receives the numeric weight, so SVG text uses nearest-weight matching like HTML.
- Outline-painted text (`GetTextOutline`) has no palette parameter; documented in the accepted gap.

Evidence: `SvgTextFontPaletteAlternatesTests` (recorded `ShapeSettings`/`FontPalette`, real outline difference
for `ss01`), `SvgTextFontPaletteIntegrationTests` (PDF colours for inline and standalone SVG, with a negative control).
