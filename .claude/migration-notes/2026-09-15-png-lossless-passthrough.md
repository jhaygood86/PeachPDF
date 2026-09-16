# Opaque PNGs embed losslessly by default; new `ImageCompression` option

An opaque PNG (a screenshot, chart, logo, QR code, line art - anything with no declared alpha channel,
no `tRNS` chunk, and not Adam7-interlaced) used to be silently re-encoded as a lossy JPEG at quality 75
before being embedded, regardless of the fact that PNG was chosen specifically for content JPEG
compresses badly (hard edges, flat color regions, thin lines). It's now embedded via byte-for-byte
`/FlateDecode` pass-through instead: the PNG's own compressed pixel data (`IDAT`), unchanged, with a
`/DecodeParms` describing PNG's own predictor/color layout. A document containing such a PNG will now
render pixel-exact instead of showing JPEG artifacts around hard edges - and the embedded file is
typically smaller too, since PNG's own per-scanline-predicted compression usually beats a lossy JPEG
re-encode for this kind of content. A QR code that previously risked becoming unscannable now always
decodes correctly.

An eligible PNG is also now always embedded at its natural pixel size - `DownscaleImages` and
`MaximumDownscaleMultiplier` no longer resize it, since a byte-for-byte pass-through embed can't be
resized (the same trade-off already made for a CMYK JPEG/TIFF). An interlaced PNG, an alpha-bearing
PNG, or a non-PNG opaque raster source (BMP/GIF/WebP/AVIF/TIFF) is unaffected by this specific change -
see below for the separate, related fix to two of those.

Separately: an opaque BMP or GIF (neither format has a lossy encoding mode at all) also stops being
silently re-encoded as lossy JPEG at its own natural display size - it now embeds via the existing raw
`/FlateDecode` RGB path instead (the same path an alpha-bearing PNG already used). A *downscaled* BMP/GIF
(display size smaller than natural) is unaffected and keeps the existing downscale-to-JPEG-at-
`DownscaleQuality` behavior, since that's an intentional, separate size/quality trade-off this change
doesn't touch.

New `PdfGenerateConfig.ImageCompression` option (`Auto`/`Lossless`/`Lossy`, default `Auto` - the behavior
described above):

- `Lossless` extends the same "never re-encode as lossy JPEG" guarantee to a *downscaled* PNG/BMP/GIF too
  - it's decoded, resampled, and re-`/FlateDecode`-encoded instead of JPEG-compressed at
  `DownscaleQuality`. Larger downscaled output, always pixel-exact regardless of size.
- `Lossy` is an explicit opt-out back to the pre-this-change default: always re-encode an opaque
  PNG/BMP/GIF as JPEG, even one that would otherwise be pass-through-eligible, for anyone who wants the
  smallest files even for diagram/line-art content and accepts the fidelity loss.

Not part of this change: an eligible PNG's embedded ICC profile (`iCCP` chunk) preservation (issue
#1106) and WebP/AVIF/TIFF lossless-encoding detection (issue #1107) - see their own, later migration
notes for what each of those added on top of this one.
