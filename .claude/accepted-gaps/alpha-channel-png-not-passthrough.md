# PNGs with a real per-pixel alpha channel never use byte-for-byte pass-through

Issue #1086 added byte-for-byte `/FlateDecode` pass-through for an opaque PNG, and a follow-up
extended it to the `tRNS` chroma-key case (grayscale/truecolor `tRNS`, and a palette `tRNS` whose
every listed entry is exactly 0 or 255 — both map onto PDF's own color-key `/Mask` mechanism, no
decode needed). A PNG with a genuine per-pixel alpha channel — color type 4 (`GrayscaleAlpha`),
color type 6 (`TruecolorAlpha`), or a palette PNG whose `tRNS` contains a partial-alpha entry
(neither 0 nor 255) — still can't pass through at all, and stays on the existing full decode +
`/FlateDecode` raw-RGB + separate `/SMask` path.

The reason is architectural, not an oversight: PDF wants an alpha-bearing image as **two
independent** `/FlateDecode` streams (the color XObject, and a separate single-channel `/SMask`
XObject), while PNG stores color and alpha **interleaved** in one filtered, deflate-compressed
`IDAT` stream. Splitting one into the other needs a real (if narrower than today's) decode:
inflate the zlib stream, undo PNG's per-scanline predictor filter, de-interleave the raw samples
into two planar buffers, then re-filter and re-deflate each independently. None of that is exposed
by PeachImage today — only the full pixel-format-converting `Image.Load` decode, or the no-inflate
raw-chunk `PngPassthrough.TryRead` (added for #1086, which by design never inflates anything)
exist. A partial-alpha palette entry hits the same wall: it isn't expressible as PDF's binary
color-key `/Mask` at all, so it needs the same real alpha-plane split as color type 4/6.

Closing this needs PeachImage to expose the unfiltered-but-still-encoded intermediate scanline
data (post-inflate, post-unfilter, pre-color-interpretation) split by channel group, or some
equivalent that lets `PdfImage.EmbedPngPassthrough` build a color-only stream plus a separate
alpha-only stream without committing to a full `Rgba32` conversion first.

Tracked as [issue #1109](https://github.com/jhaygood86/PeachPDF/issues/1109).
