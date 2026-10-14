# brightness(), contrast() and invert() can be written without /TR (PortableColorFilters)

MuPDF ignores an ExtGState `/TR` (and `/TR2`, which only adds `/Default`), so the three channel-independent filter functions drew
unfiltered there. `PdfGenerateConfig.PortableColorFilters` (default off) writes them as blend-mode layers instead:
`PdfColorMatrixBlendLayers.Build` turns the matrix into opaque full-tile fills, and
`PdfGraphicsRenderer.DrawImageWithBlendLayers` paints the content, then those fills, inside one isolated wrapper form that is placed
through an `/Alpha` soft mask whose `/G` is the content form. Per channel `clamp(m*c + b)` is built from Difference (invert),
Multiply, ColorDodge (slope above one, pivot 0), Screen and ColorBurn (slope above one, pivot 1); `contrast(k>1)` is ColorDodge
`(k-1)/(2k)` then ColorBurn `2/(k+1)`. `PdfColorMatrixBlendLayersTests` evaluates the layers with the PDF blend formulas and holds
them to the matrix over a sweep of slopes and offsets.

**Found by running it.**
- A fill with its own alpha (`ca < 1`) double-counts alpha: the group's alpha already carries the content's, and the soft mask applies
  it again. Every layer is therefore opaque, and partial strength is folded into the fill value (`invert(0.5)` is Multiply 0 then
  Screen 0.5).
- Masking each fill inside the group instead of masking the wrapper raises an antialiased edge's alpha to `2a - a^2` and fattens
  shapes.
- A blend over a partly transparent backdrop lets the fill colour in with weight `1 - alpha` (ISO 32000-1 §11.3.6), and a group cannot
  un-premultiply. So the result is exact on opaque content and off on `rgba()` content and antialiased edges (about 60 of 255 in the
  worst probed case, `invert()` on 50% red). That is why the option is opt-in and the default stays `/TR`, which ignores alpha.
- The default `/TR` path is itself wrong in PDFium for gradients: the `css_filter` showcase's brightness/contrast/invert gradients come
  out unfiltered (only the vector text is filtered). So `/TR` is not a usable oracle for shadings; the portable output was checked
  against the CSS formulas applied to the baseline swatch of the same document instead.

**Evidence.** `css_filter_portable` rasterized in PDFium and MuPDF: all eight swatches agree between the two readers and are within
0-6 of 255 of the CSS formulas, including `brightness() contrast()` chained. The opaque-swatch spike matched the issue's PDFium
numbers ((63,236,215), (0,187,159), (213,98,112)) in both.

**Not done.** No CLI flag. No per-element choice, and no automatic fallback for translucent content (a tile does not know whether its
content is translucent). Making it the default is a decision for a later change, given the PDFium gradient finding above.
