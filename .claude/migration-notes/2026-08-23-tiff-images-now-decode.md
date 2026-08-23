# TIFF images now decode

Previously (through the released v0.9.12): raster decoding ran through PeachImage 0.2.2, which
implements JPEG, PNG, BMP, GIF, WebP, and AVIF only. An `<img>`, `background-image`, or SVG `<image>`
pointing at a `.tif`/`.tiff` file (or a `data:image/tiff;...` URI) rendered as an unresolved replaced
element, the same as any other undecodable image.

Now: bumping to PeachImage 0.4.1 adds TIFF decode (both byte orders, uncompressed/LZW/PackBits
compression, 1/2/4/8/16-bit grayscale/RGB/palette/CMYK, including the Predictor=2 LZW variant), so
these images render normally on both target frameworks. No PeachPDF code change was needed — raster
format is auto-detected from the file's bytes (see `docs/architecture.md`'s "Image loading and
decoding" section), so a decodable format PeachImage adds is picked up automatically. `MimeTypeResolver`'s
built-in MIME-type fallback map was *not* updated to add `tif`/`tiff` entries (unlike the WebP/AVIF
migration) — images are recognized by their bytes regardless of resolved content type, so decode works
either way; a `.tif` file with no OS MIME association just falls back to `application/octet-stream` for
`Content-Type` purposes. Tiled organization, planar storage, compression other than none/LZW/PackBits,
BigTIFF, floating-point/signed samples, and photometric interpretations outside
grayscale/RGB/palette/CMYK (YCbCr, LogLuv, Lab) remain unimplemented in PeachImage and are not
decodable. TIFF encode is not implemented in PeachImage at all, which is irrelevant here — a PDF can
only embed a JPEG or raw bitmap stream, never a TIFF file directly.
