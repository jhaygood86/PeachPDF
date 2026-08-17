# WebP and AVIF images now decode

Previously: raster decoding (StbImageSharp, then PeachImage 0.1.x/0.2.0's initial JPEG/PNG/BMP/GIF
support) never included WebP or AVIF. An `<img>`, `background-image`, or SVG `<image>` pointing at a
`.webp`/`.avif` file or `data:image/webp;...`/`data:image/avif;...` URI rendered as an unresolved
replaced element, the same as any other undecodable image.

Now: PeachImage decodes WebP (VP8 lossy and VP8L lossless, including alpha) and AVIF (baseline still
images - intra-frame AV1, HEIF `grid` composites, alpha, 8/10-bit depth) on both target frameworks, so
these images render normally. `MimeTypeResolver`'s built-in MIME-type fallback map also gained `webp`/
`avif` extension entries to match. Encoding to either format is not exposed by PeachPDF (irrelevant here
- a PDF can only embed a JPEG or raw bitmap stream, never a WebP/AVIF bitstream directly), and AVIF has
no encoder in PeachImage at all.
