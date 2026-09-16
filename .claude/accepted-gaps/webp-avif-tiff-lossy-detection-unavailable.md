# WebP/AVIF/TIFF are excluded from `ImageCompression`'s lossless-source protection

Issue #1086's `PdfGenerateConfig.ImageCompression` (`Auto`/`Lossless`/`Lossy`) governs whether an opaque
raster source is ever silently re-encoded as lossy JPEG. `PeachImageSource`'s `IsLosslessSourceFormat`
flag decides which sources that protection applies to, and it's a plain static fact table:
`formatName is "png" or "bmp" or "gif"`. That's safe because none of those three formats has a lossy
encoding mode at all — a decoded PNG/BMP/GIF source is *always* lossless by construction, regardless of
how it was produced.

WebP, AVIF, and TIFF don't have that property: each format supports both a lossy and a lossless encoding
mode (WebP's VP8/VP8L split, AVIF's AV1 lossless tool, TIFF's optional JPEG-in-TIFF compression), and
PeachImage's current public API (`ImageInfo`, `Image.HasAlpha`) gives no way to tell which mode a given
decoded source actually used. Including them in `IsLosslessSourceFormat` without that signal would risk
`Auto`/`Lossless` "protecting" a source that was already lossy-encoded — the protection would spend a
larger, still-lossy-derived `/FlateDecode` re-embed for no actual fidelity gain over the JPEG re-encode
it was trying to avoid.

So all three formats stay on their pre-#1086 behavior (an opaque WebP/AVIF/TIFF source still gets
JPEG-re-encoded under `ImageCompression.Auto`/the historical default) regardless of which
`ImageCompression` value is set.

Closing this needs a PeachImage API surface exposing whether a specific decoded WebP/AVIF/TIFF source
used a lossy or lossless encoding — e.g. an `ImageInfo.IsLosslessEncoding` bool alongside the existing
JPEG-specific flags (`IsAdobeInvertedCmyk`/`IsYcck`). Once available, `IsLosslessSourceFormat`'s
computation can consult it for these three formats instead of leaving them out entirely.

Tracked as [issue #1107](https://github.com/jhaygood86/PeachPDF/issues/1107).
