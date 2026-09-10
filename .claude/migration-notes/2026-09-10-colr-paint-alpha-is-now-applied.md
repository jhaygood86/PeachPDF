# A COLR paint's own alpha is now applied

_Landed 2026-09-10._

**Before:** a `COLR` v1 paint that carries an alpha — `PaintSolid`'s `Alpha`, or a gradient
`ColorStop`'s — painted as though that alpha were nearly zero. `ColorGlyphPainter.ResolveColor`
scaled it as if `XColor.A` were a 0..255 byte when it is in fact a 0..1 double, so an alpha of 0.2
rounded to a fully transparent `0` and anything above 0.5 to `1`, i.e. 1/255. Every non-opaque COLR
paint was therefore invisible in practice, and whether it left a `/ca 0` in the content stream at all
depended on unrelated graphics-state history.

**Now:** the alpha is scaled correctly and reaches the content stream as the constant alpha it is.

**What a document author sees:** color-emoji artwork that uses translucent layers gains detail it was
silently dropping. The clearest case is Noto Color Emoji's flags, which outline themselves with a
`#1A1A1A` layer at alpha 0.2 — the light grey border in Noto's own reference rendering. Those borders
were missing; they now appear, and match the reference. Nothing that was already opaque changes.

Verified against the real font by rasterizing 🇯🇵 through PDFium and comparing with Google Fonts'
specimen for Noto Color Emoji. Regression guard:
`ColorGlyphRenderingIntegrationTests.ColrPaintAlpha_ReachesTheContentStreamAsAConstantAlpha`, which
resolves the ExtGState by name and requires it to be in force at the border layer's own fill rather
than merely present in the file.
