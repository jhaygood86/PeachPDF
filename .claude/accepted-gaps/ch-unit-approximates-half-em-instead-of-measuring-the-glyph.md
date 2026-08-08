# `ch` approximates `0.5em` instead of measuring the font's real "0" glyph

Per CSS Values and Units §6.2: *"In the cases where it is impossible or impractical to determine the
measure of the '0' glyph, it must be assumed to be 0.5em wide."* PeachPDF always takes this fallback
(`Length.ToPixels`'s `Unit.Ch => 0.5 * emFactor * Value` arm) rather than measuring the actual advance
width of the current font's "0" (U+0030) glyph — the same approximation this engine already uses for
`ex`'s x-height (`Unit.Ex => emFactor / 2 * Value`, the identical formula).

Real per-font measurement would need to thread a measurement basis through the value-resolution
pipeline (`CssValueParser.ParseLength`/`Length.ToPixels`) the way `emFactor` already is, since most
length-resolution call sites (`CssLayoutEngine.GetActualMarginLeft`, flex/grid track sizing, etc.) have
no font-measurement context in scope, or resolve a box's width/margin before any of its own text has
been shaped — and it would need to get the `PixelsPerPoint` device-scaling direction right for a new
glyph-metric code path, the same class of mistake fixed for `@page` em/rem margins and `NoEms` in
commit `8b21d6f4`. This is real accuracy loss for a font whose "0" glyph isn't close to half an em wide,
but the `0.5em` fallback is explicitly spec-legal, not a spec violation. Tracked as
[#678](https://github.com/jhaygood86/PeachPDF/issues/678).
