# Hairline gradient background (`bottom / 100% 0.25pt no-repeat`) failed the whole render (#2458)

A sized gradient layer is rendered once into a tile (`PdfCanvas.CreateTile` -> `PdfForm`). `PdfForm`'s constructor rejected any
`/BBox` under 1pt in either axis (a leftover from the PDFsharp `XForm` it derives from), so a 0.25pt-high strip threw an
`ArgumentNullException` that surfaced as `HtmlRenderException: Exception in box paint` with the real cause three exceptions deep.

Fix: the guard now rejects only an empty or non-finite box. A sub-point `/BBox` is valid PDF. Verified by rasterizing the
output with PDFium and MuPDF (the strip paints at the bottom edge) and by `GradientHairlineBackgroundTests` (div and td).

Trap: the symptom names the paint pass, not the cause - walk `InnerException` to the bottom first.
