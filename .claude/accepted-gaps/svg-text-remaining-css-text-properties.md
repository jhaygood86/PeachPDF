# SVG text: CSS text/font properties that still do not apply

Tracked in [issue #1579](https://github.com/jhaygood86/PeachPDF/issues/1579) (the earlier tracker, #1572, wired everything else).

Not applied to SVG `<text>`/`<tspan>`:

- **Wrapped-text properties** — `text-indent`, `line-height`, `hyphens`, `text-align`, the wrapping `white-space` modes and hard line
  breaks under `pre`. SVG 2 defines them only for text laid out with `inline-size`/`shape-inside`; this repo has neither and plain SVG
  text has no line boxes, so there is nothing for them to act on. A newline in preserved text is a space.
- **`font-synthesis*`** — there is no switch to suppress faux bold/italic anywhere (HTML either): the decision is made inside the font
  match (`TypefaceMatch.Synthesis`) and reaches the renderers through `Font.SyntheticStyle`, so honouring it needs a public
  `PeachDrawing.Core` API change and a new font-cache-key dimension. SVG has no small-caps or sub/superscript synthesis either (see
  [font-variant-position-synthesis-scope](font-variant-position-synthesis-scope.md)).
- **`font-size-adjust`**, **`font-language-override`**, **`-webkit-text-stroke`**, **`text-rendering`** — not implemented for HTML text
  either; no rendering hook exists.
- **`textLength` with `lengthAdjust="spacingAndGlyphs"`**, and `textLength` on `<textPath>` or under a vertical `writing-mode`.
- **`text-shadow`** on `<textPath>` glyphs; a glyph with an explicit `rotate=""` gets an unblurred shadow (a blur layer's bounds are
  not computed through the glyph's rotation).
- **Baselines** are approximated from font metrics (no `BASE` table), only under horizontal writing, and not on `<textPath>`.
- Text painted as outlines (gradient/pattern fill, stroke) is drawn in the font's default palette, since `Canvas.GetTextOutline` has no
  palette parameter.
