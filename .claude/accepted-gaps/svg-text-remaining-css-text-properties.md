# SVG text: CSS text/font properties that still do not apply

Tracked in [issue #1579](https://github.com/jhaygood86/PeachPDF/issues/1579) (the earlier tracker, #1572, wired everything else).

Not applied to SVG `<text>`/`<tspan>`:

- **Wrapped text** (`inline-size`/`shape-inside`, with `text-indent`, `line-height`, `hyphens`, `text-align` and the wrapping `white-space` modes) is
  implemented for horizontal writing modes (`SvgRenderer.WrappedText.cs`). What it leaves out: vertical `writing-mode` (the inline-size is ignored
  there); `shape-inside`/`shape-subtract` with `url()` references, `path()` or a reference box (only `inset()`/`circle()`/`ellipse()`/`polygon()`
  resolve, a curve as a 96-sided polygon); `shape-margin`/`shape-padding`; `text-indent: hanging`/`each-line`, `text-align-last`,
  `text-wrap`/`white-space-collapse`, `hyphenate-limit-chars`; `break-spaces` behaves as `pre-wrap` (no break after preserved spaces); per-character
  `x`/`y`/`dx`/`dy` and `textLength` are ignored in wrapped text; a word wider than every stretch of a shape is dropped with the text after it.
  A line's height is the largest `line-height` among the glyphs on it, with leading split evenly (no strut for an empty inline box).
- **`font-synthesis-small-caps`/`-position`** have nothing to switch off in SVG: it has no small-caps or sub/superscript synthesis (see
  [font-variant-position-synthesis-scope](font-variant-position-synthesis-scope.md)). `-weight`/`-style` are honoured.
- **`font-size-adjust`'s `ic-height`** is measured as `ic-width` (the font layer has no vertical ideograph advance).
- **Baselines** read the `BASE` table for the font's default script only (the text's script is not passed to the lookup), and the
  per-script `MinMax` extents and the version 1.1 variable-font deltas are not read.
- **`text-rendering`** has an effect only for `optimizeSpeed` (kerning and optional ligatures off); the other keywords are no-ops
  because PDF output is unhinted vector content, so there is no hinting or geometry switch for them to flip.
- **`-webkit-text-stroke`** is always painted over the fill: `paint-order` does not apply to HTML text, and an SVG text stroke follows
  `paint-order`. A font with no decodable outlines is drawn unstroked.
- **`font-language-override`** selects the language system for `GSUB` features only; `GPOS` kerning always reads the font's default
  language system.
- Text painted as outlines (gradient/pattern fill, stroke) is drawn in the font's default palette, since `Canvas.GetTextOutline` has no
  palette parameter.
