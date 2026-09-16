# Losslessly-encoded WebP/AVIF/TIFF sources now get `ImageCompression`'s lossless protection

Before: `PdfGenerateConfig.ImageCompression`'s `Auto`/`Lossless` protection against a silent lossy
JPEG re-encode only ever applied to PNG, BMP, and GIF sources. An opaque WebP, AVIF, or TIFF image was
always re-encoded as JPEG at natural size under every `ImageCompression` value, even one that was
itself losslessly encoded (WebP's VP8L mode, AVIF's lossless AV1 tool, or an uncompressed/LZW/PackBits
TIFF) — PeachPDF had no way to tell which encoding mode a decoded source had actually used.

Now (PeachImage 0.4.6's `ImageInfo.IsLosslessEncoding`): a WebP/AVIF/TIFF source that was itself
losslessly encoded gets the same treatment as an opaque PNG/BMP/GIF — under `Auto` it's never
re-encoded as JPEG at natural size, and under `Lossless` not even when downscaled. A *lossy*-encoded
WebP/AVIF/TIFF source is unaffected and keeps re-encoding as JPEG under every mode, since there's no
fidelity to protect there. `ImageCompression.Lossy` is unaffected either way — it always forces the
JPEG re-encode regardless of source format or encoding mode, same as before.

Tracked as [issue #1107](https://github.com/jhaygood86/PeachPDF/issues/1107).
