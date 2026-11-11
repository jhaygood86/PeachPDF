# Raster-fallback images (blurred shadows, filters, backdrop filters) are embedded without a PNG round trip

- A region PeachPDF has to draw as pixels (a blurred `box-shadow`, a CSS or SVG filter, a `backdrop-filter`, a flattened transparency group) used to be
  encoded as a PNG file and read back before it was embedded. It is now embedded straight from its pixels: a Flate stream of PNG-row-filtered RGB, and for
  a surface with partial transparency a separate alpha plane as a soft mask. The decoded pixels are the same; the compressed bytes of those image streams
  are not, so a PDF that has such regions is not byte-for-byte what the previous release wrote.
- Across the showcase corpus the 27 PDFs affected are 13% smaller in total (5.92 MB to 5.15 MB), `svg_filters_raster` and `css_filter_raster` the most;
  a few grew by 1-6% (small text rasters under the `text-hinting` showcases and `perspective_3d_transforms`), because the row filter choice is simpler
  than the PNG encoder's. Every page was rasterized with PDFium and MuPDF before and after and is pixel-identical.
- Two kinds of region still go through a PNG, because a PNG of them is a different kind of PDF image (a color-keyed or `/Indexed` one) that viewers do not
  draw exactly like RGB plus a soft mask: hard-edged transparency (every pixel opaque or clear, as with `text-rendering` anti-aliasing off) and an opaque
  region of 256 colors or fewer. Their output is unchanged.
- Confirm with the previous tag: `git show v0.9.26:src/drawing/PeachDrawing.PDF/Backend/RasterEmbedding.cs` encoded every such region as a PNG
  (`RasterSurfaceEncoding.EncodePng`) and read it back with `PdfRasterImage.FromStream`.
