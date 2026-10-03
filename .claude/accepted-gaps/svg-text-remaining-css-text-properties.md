# SVG text: CSS text/font properties that still do not apply

Tracked in [issue #1579](https://github.com/jhaygood86/PeachPDF/issues/1579) (the earlier tracker, #1572, wired everything else).

Not applied to SVG `<text>`/`<tspan>`:

- **Wrapped-text properties** — `text-indent`, `line-height`, `hyphens`, `text-align`, the wrapping `white-space` modes and hard line
  breaks under `pre`. SVG 2 defines them only for text laid out with `inline-size`/`shape-inside`; this repo has neither and plain SVG
  text has no line boxes, so there is nothing for them to act on. A newline in preserved text is a space.
- **`font-synthesis-small-caps`/`-position`** have nothing to switch off in SVG: it has no small-caps or sub/superscript synthesis (see
  [font-variant-position-synthesis-scope](font-variant-position-synthesis-scope.md)). `-weight`/`-style` are honoured.
- **`font-size-adjust`'s `ic-height`** is measured as `ic-width` (the font layer has no vertical ideograph advance).
- **`font-language-override`**, **`-webkit-text-stroke`**, **`text-rendering`** — not implemented for HTML text
  either; no rendering hook exists.
- **Baselines** are approximated from font metrics (no `BASE` table), only under horizontal writing, and not on `<textPath>`.
- Text painted as outlines (gradient/pattern fill, stroke) is drawn in the font's default palette, since `Canvas.GetTextOutline` has no
  palette parameter.
