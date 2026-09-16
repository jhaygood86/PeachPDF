# Eligible GIF sources now embed via /LZWDecode pass-through

Before: a GIF was always fully decoded to `Rgba32` and re-embedded as a raw `/FlateDecode` RGB stream
(or, under `ImageCompression.Lossy`, a lossy JPEG re-encode) — the same path any other opaque
non-pass-through raster source used.

Now (PeachImage 0.4.6's `GifPassthrough`/`GifPassthroughInfo`): a GIF whose frame isn't interlaced,
whose LZW minimum code size is exactly 8 (GIF starts codes at `MinCodeSize + 1` bits with clear code
`1 << MinCodeSize`, matching the fixed 9-bit start/clear-code-256 convention `/LZWDecode` always
uses), and whose frame covers its full logical canvas, embeds the same LZW code values its own
encoder produced as `/LZWDecode` with an `/Indexed` color space, instead of being decoded and
re-encoded. This is a bit-repack, not a literal byte copy: GIF packs codes least-significant-bit-first
and grows code width one bit at a slightly different code count than PDF/TIFF's
most-significant-bit-first `/LZWDecode` convention does — `PdfImage.RepackGifLzwForPdf` re-packs the
underlying codes rather than reusing GIF's bytes directly, still without a full LZW
decompress-then-recompress round trip. A declared transparent color index (GIF transparency is always
exactly one palette index) writes as a PDF color-key `/Mask` array, the same mechanism a PNG's
`tRNS`-derived transparency already used. Like a pass-through-eligible PNG, an eligible GIF is always
embedded at natural size, regardless of `DownscaleImages`.

A GIF that doesn't qualify (small palette, interlaced, or a partial-canvas frame) is unaffected and
keeps using the existing raw-`/FlateDecode` decode path. `ImageCompression.Lossy` still forces a lossy
JPEG re-encode for an eligible-but-opaque GIF the same way it already did for PNG; a GIF with a declared
transparent index stays pass-through-embedded under every `ImageCompression` value, since JPEG can't
represent that transparency at all.

Tracked as [issue #1110](https://github.com/jhaygood86/PeachPDF/issues/1110).
