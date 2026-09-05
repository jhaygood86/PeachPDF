# SVG `text-decoration` v1 scope

`text-decoration-line`/`-style`/`-color` (issue #533) paint for horizontal-tb, non-`<textPath>`,
non-per-character-rotated `<text>`/`<tspan>` only. A vertical-writing-mode `<text>` (`writing-mode:
vertical-rl`/`vertical-lr`) and `<textPath>` glyphs (laid out and painted entirely by
`SvgRenderer.RenderTextPath`, a separate code path `PaintTextDecorations` is never called from) don't
paint a decoration line at all yet — a real per-glyph-rotated/along-a-curve decoration line is a
materially different geometry problem (following the path's own tangent, or running down a column)
than the straight horizontal segment this first cut implements. A glyph carrying an explicit
per-character `rotate=""` is skipped for the same "no well-defined single straight line" reason a
rotated glyph's decoration would need its own rotated-segment geometry.

Also out of scope for this pass: no small-caps *synthesis* fallback exists for SVG
`font-variant-caps: small-caps`/`all-small-caps` the way HTML's `CssBox.AddWord` has (a scaled-lowercase-glyph
substitute when the resolved font lacks real `smcp`/`c2sc` GSUB support) — SVG only ever requests the
real OpenType features, silently inert on a font that lacks them.

Needs a tracking issue filed (not yet done as of this note) before this can be considered a fully
recorded gap per this repo's own convention pairing a deliberate spec-adjacent scope decision with a
GitHub issue.
