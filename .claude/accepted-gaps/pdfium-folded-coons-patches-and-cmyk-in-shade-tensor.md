# shade-tensor: folded Coons patches and a CMYK mesh

PDFium's `shade-tensor` still differs by about 9% of pixels, from two things that are not importer defects:

- **Four tiny Coons patches whose corners coincide (`Shj`..`Shm`).** Their boundary curves are closed loops. The patch's surface as
  PDF 8.7.4.5.7 defines it (the bicubic surface through the Coons control points, checked by evaluating it independently) is a filled
  diamond, which is what this importer draws; PDFium draws the loops as a cross, a star and a leaf with petals because it fills curved
  boundary quads rather than sampling the surface. A finer or jump-aware grid made no difference (9.1% -> 9.3%), so none was kept.
- **`Shi` is a DeviceCMYK mesh**, so its colors differ by PDFium's CMYK table (see
  [pdfium-specific-jpx-and-cmyk-choices.md](pdfium-specific-jpx-and-cmyk-choices.md)).
