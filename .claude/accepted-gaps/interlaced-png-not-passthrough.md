# Interlaced PNGs never use byte-for-byte pass-through

Issue #1086 added byte-for-byte `/FlateDecode` pass-through for an opaque, non-interlaced PNG
(`PeachImageSource.DecodePng`/`IsPassthroughEligible`, `PdfImage.EmbedPngPassthrough`). Interlaced
(Adam7) PNGs are deliberately excluded and stay on the pre-#1086 decode path instead.

PDF's `/FlateDecode` `/DecodeParms` predictor (`/Predictor 15`, PNG-style) describes a sequential,
top-to-bottom scanline layout with no concept of Adam7's seven interleaved passes. An interlaced
PNG's `IDAT` stream simply isn't shaped like a PDF image stream's data is expected to be — there's
no PDF-legal `/DecodeParms` value that would make a reader reconstruct it correctly.

Unlike the other PNG pass-through gaps (see
[alpha-channel-png-not-passthrough.md](alpha-channel-png-not-passthrough.md) and
[gif-not-byte-for-byte-passthrough.md](gif-not-byte-for-byte-passthrough.md)), this one has no
realistic "PeachImage adds an API, PeachPDF wires it up" closing path at all — true byte-for-byte
pass-through of Adam7 data is impossible in PDF's image model regardless of what PeachImage could
expose. The only way to get *pixel-exact* output for an interlaced source without the current
decode-and-recompress cost would be a de-interlace-then-re-deflate step (inflate → unfilter → 
reassemble in sequential order → re-filter → deflate) — a real re-encode, not a pass-through, and
one that still needs the same kind of scanline-level access `alpha-channel-png-not-passthrough.md`'s
tracking issue is already asking for. Not judged worth a second, narrower issue on top of that one.

This is not a spec violation — the existing decode path this falls back to has always produced
fully spec-compliant PDF output, just not a byte-for-byte, smallest-possible embed. This is a
continuation of pre-#1086 behavior, not a new limitation introduced by that change, so (unlike the
ICC and lossy-detection gaps #1086 also left behind) this one has no tracking issue of its own.
