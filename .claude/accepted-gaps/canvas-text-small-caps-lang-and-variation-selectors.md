# Canvas 2D text: small caps, `lang`, variation selectors, complex-script centering

`fontVariantCaps` is passed to the shaper as a caps mode, so a font that has `smcp` draws small caps; for one that has not (the bundled serif), nothing is synthesized, and
`2d.text.fontVariantCaps2` fails. A `lang` is passed as the shaper's `Language`, which does not select a `locl` language system for `tr`
(`2d.text.measure.lang`). VS15/VS16 do not change the advance (`2d.text.variationSelectors`), and U+3000 in a font without it measures no ink
(`2d.text.measure.actualBoundingBox.whitespace`, `2d.text.draw.align.center.complex-shaping`). Tracked in issue 2499.

The selection and caret tests that compare a canvas with the DOM (`selection-rects`, `index-from-offset`, `advance-to-position`) still differ for strings the DOM's own text layout draws
differently: U+1F3B6 takes no width there where the canvas uses a fallback emoji font, and a right-to-left paragraph of CJK brackets puts its carets one character away. Those are the
DOM layout's, not the canvas's.
