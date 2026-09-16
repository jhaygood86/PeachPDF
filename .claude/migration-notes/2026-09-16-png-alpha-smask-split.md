# PNGs with a real per-pixel alpha channel now pass through as color + /SMask

Before: any PNG with a genuine per-pixel alpha channel — color type 4 (`GrayscaleAlpha`), color type 6
(`TruecolorAlpha`), or a palette PNG whose `tRNS` contained a partial-alpha entry (neither 0 nor 255) —
was always fully decoded to `Rgba32` and re-embedded as a raw `/FlateDecode` RGB stream plus a separate,
non-predictor-compressed `/SMask` built from the decoded alpha buffer.

Now (PeachImage 0.4.6's `PngAlphaSplit`): such a PNG, when not interlaced (and, for color type 4/6,
8-bit), passes through instead. For color type 4/6, PeachPDF splits the PNG's own interleaved
color+alpha `IDAT` data into two independent PNG-row-filtered `/FlateDecode` streams — a color XObject
and a child `/SMask` — without a full pixel decode. For a palette source with partial-alpha `tRNS`, the
color side needs no new data at all (the original indexed `IDAT`/palette embed unchanged); only the
alpha plane is newly split out and attached as `/SMask`. Both the color stream and the `/SMask` stream
carry the same PNG-predictor `/DecodeParms` the opaque/chroma-key pass-through path already used — a
visible difference from the old raw-alpha-mask shape if you're inspecting the PDF's own structure, not a
behavior change in how the alpha renders.

An interlaced alpha-bearing PNG, or (in PeachImage's current version) a 16-bit-per-channel color type
4/6 source, isn't split and keeps using the pre-existing full decode + raw `/SMask` path unchanged.

`ImageCompression.Lossy` doesn't force a JPEG re-encode for an alpha-split-eligible source either — JPEG
has no alpha channel to hold the transparency in at all, the same carve-out a `tRNS` color-key mask
already had.

Tracked as [issue #1109](https://github.com/jhaygood86/PeachPDF/issues/1109).
