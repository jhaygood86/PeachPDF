# A gradient fill with a translucent stop inherited the previous fill's alpha

An SVG shape with a `fill-opacity` followed by a shape filled with a gradient that has a `stop-opacity` drew the gradient about half as strong as its stops say (measured: 0.43 alpha where 0.75 was
expected at the middle of the gradient; PDFium and MuPDF agreed). Without the earlier `fill-opacity` shape, or without the `stop-opacity`, the gradient was right, which is what pointed at the PDF graphics state.

## Cause

`PdfGraphicsState.RealizeBrush`, the gradient branch, had two arms: a gradient with a translucent stop emitted its soft-mask ExtGState (`gs`) and nothing else; only the `else` arm (an opaque gradient) reset the ambient
fill alpha (`/ca`) the last solid fill left behind. So the translucent gradient was multiplied twice: by its own soft mask and by the leftover `/ca`. The reset
([2026-09-16](2026-09-16-gradient-fill-inherits-leftover-fill-alpha.md)) had been written for the opaque case only, because that is the one it was found with. The leftover alpha is not always a faint one: after
`color: transparent` text (`/ca 0`) a translucent `background-clip: text` gradient was *invisible* (measured: one ink band where two are expected), after a `fill-opacity: .5` shape it came out half as strong.

## Fix

The reset now runs for every fill-side gradient, before the soft-mask `gs` (`!isForPen`; the stroke side already used `1.0` for a brush). A shape's own `fill-opacity` on a gradient is not what leaked: that alpha is folded into the gradient's stops, not into `/ca` (a gradient shape painted first is identical before and
after; one painted after an alpha shape changes, because of the leftover alpha). A gradient stroke is untouched (`!isForPen`; the stroke side already used alpha 1 for a brush).

## Evidence

`GradientFillAlphaTests.TranslucentGradientFillAfterAFillOpacityShape_ResetsTheAmbientFillAlpha` and `...AfterTransparentTextFill_ResetsTheTextsZeroAlpha` assert that one of the `gs` operators written right before the gradient's
`/Pattern cs` sets `/ca 1` and another carries the soft mask, in either order (the two are independent graphics-state keys); both fail without the fix. The `background_clip_text` showcase gained a third heading with a translucent first stop. The repro rasterized through PDFium and MuPDF
matches a control without the earlier shape to the pixel (`(255,1,1)` to `(255,122,122)` down the gradient).
