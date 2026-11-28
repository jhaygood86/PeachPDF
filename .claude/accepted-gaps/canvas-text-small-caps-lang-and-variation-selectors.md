# Canvas 2D text: small caps, `lang`, variation selectors, complex-script centering

Small capitals are synthesized for a font with no `smcp` (`PeachDrawing.Core.SyntheticCaps`: capitals at 0.72 of the size, `petite-caps` the same); `unicase` and `titling-caps` are not, and
the pieces are drawn in a plain `TypefaceFont`, so a synthetic bold or italic of the original is not carried over, and non-ASCII text takes its width from the measure rather than the
paragraph layout. VS15/VS16 do not change the advance (`2d.text.variationSelectors`), and U+3000 in a font without it measures no ink
(`2d.text.measure.actualBoundingBox.whitespace`, `2d.text.draw.align.center.complex-shaping`). Tracked in issue 2499.

The selection and caret tests that compare a canvas with the DOM (`selection-rects`, `index-from-offset`, `advance-to-position`) still differ for strings the DOM's own text layout draws
differently: U+1F3B6 takes no width there where the canvas uses a fallback emoji font, and a right-to-left paragraph of CJK brackets puts its carets one character away. Those are the
DOM layout's, not the canvas's.
