# A transparent image's soft mask now interpolates like its color plane

`PdfImage.cs` writes `/Interpolate true` on a raster image's parent Image XObject whenever the source
asked for it (and PDF/A doesn't forbid it) via the existing `AllowInterpolate` property, but the two
places that build a child `/SMask` grayscale Image XObject next to it — `EmbedPngPassthrough`'s PNG
alpha-split path and `ReadTrueColorMemoryBitmap`'s decoded-bitmap fallback — never wrote the same key on
the SMask dictionary itself. Since `/Interpolate` defaults to `false` per spec, a reader is free to
resample the color plane smoothly while sampling the alpha silhouette with nearest-neighbor, producing a
visibly jagged/stair-stepped edge on an enlarged transparent PNG even though the color content looks
smooth.

## The load-bearing rule

`AllowInterpolate` was already the right per-document/per-image guard (`_image.Interpolate &&
PdfAConformance == None`) - the fix is just to call it a second time, once per `/SMask` construction
site, and write the same `Elements[Keys.Interpolate] = PdfBoolean.True` onto the child dictionary that
the parent already gets. No new state or guard needed.

The 1-bit hard `/Mask` (`/ImageMask true`) that `ReadTrueColorMemoryBitmap` also builds for compatibility
with older readers is a different PDF construct - a stencil, not a sampled channel - and does **not** get
`/Interpolate`; only the 8-bit `/SMask` object does.

## Evidence

`PngAlphaSplitIntegrationTests.TruecolorAlpha_SMaskAlsoGetsInterpolateTrue` and
`.InterlacedAlpha_SMaskGetsInterpolateTrueButHardMaskDoesNot` cover the pass-through and fallback paths
respectively, scoping their assertions to each object's own dictionary (split on `endobj`, same
precedent as `RadialGradientIntegrationTests.ShadingPatternMatrices`) so a hit on the parent or the hard
mask can't be mistaken for a hit on the SMask. `PdfAConformanceTests.
TransparentImage_UnderPdfAConformance_SMaskAlsoNeverGetsInterpolateTrue` confirms the PDF/A guard still
suppresses it everywhere.

Beyond the structural tests, the issue's own repro (`docs/assets/img/peach.png` scaled to 160px CSS
size) was rendered before/after the fix and rasterized with both PDFium and MuPDF per this repo's
paint-verification convention. PDFium's default rasterization showed no pixel difference between the two
PDFs (it appears to smooth-scale image data regardless of the `/Interpolate` flag), but MuPDF's did -
extracting just the alpha channel and zooming into the peach's leaf/stem contour showed a visible
staircase pattern before the fix and a smooth, properly interpolated silhouette after, with no fringe or
halo introduced at the edge.
