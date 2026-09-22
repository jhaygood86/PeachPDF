# `font-variant-position` synthesis: two deliberately narrower cases

`font-variant-position: sub|super` resolves the real OpenType `subs`/`sups` GSUB features when the
resolved font has them, and otherwise synthesizes the variant (a reduced-size face plus a baseline
shift, both taken from the font's own `OS/2` recommended metrics). Two places are narrower than a
literal reading of CSS Fonts 4.

- **SVG text never synthesizes.** `SvgTreeBuilder.ComputeFontContext`/the run-building pass request
  the real feature and gate it on the resolved font's GSUB support exactly as
  `DerivedStyle.ActualFontVariantPosition` does for HTML, but a font that lacks `subs`/`sups` leaves
  an SVG run at `normal` rather than synthesizing. The spec makes synthesis the required fallback
  ("If, in a given run, one such glyph is not available for a character, all the characters in the
  run are rendered using synthesized glyphs"). HTML synthesizes inside `CssBox.AddWord`, by giving
  the word a scaled face (`ScaledFontKind.SubSuperscript`) that `CssBox.ResolveWordFont` then picks,
  and shifting its baseline in `FragmentPainter.Text`; SVG text runs share neither mechanism. This is
  the same shape as the already-accepted "no small-caps synthesis for SVG" limitation recorded in
  [svg-text-decoration-v1-scope.md](svg-text-decoration-v1-scope.md).

- **Small-caps synthesis wins when both would synthesize on one element.** With
  `font-variant-caps: small-caps` and `font-variant-position: super` on the same element, and a font
  supplying neither feature, the word takes the small-caps face; the superscript baseline shift still
  applies, but the run is not additionally scaled for the superscript.
  `CssBox.ResolveWordFont` names exactly one face per word — deliberately, since measurement and
  paint share it and must never disagree about which face a word was drawn in — so composing the two
  would need a face at the product of the two scales and a second discriminator value.

Both filed as [issue #1266](https://github.com/jhaygood86/PeachPDF/issues/1266).
