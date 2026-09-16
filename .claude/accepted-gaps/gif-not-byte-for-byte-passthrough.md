# GIF never uses byte-for-byte pass-through

Issue #1086 (and its `tRNS` follow-up) gave PNG byte-for-byte `/FlateDecode` pass-through for the
opaque and chroma-key-transparent cases. GIF has no equivalent: a GIF source is always fully
decoded to `Rgba32` (`PeachImageSource.Decode`'s generic fallback) and re-embedded via
`PdfImage.ReadTrueColorMemoryBitmap`, regardless of `PdfGenerateConfig.ImageCompression` (which
only ever decides *whether* that re-embed prefers lossy JPEG or raw `/FlateDecode` — see
`IsLosslessSourceFormat` — not whether decoding happens at all).

GIF's transparency (a single "transparent color index" per frame, declared via the Graphic Control
Extension) would map onto PDF's color-key `/Mask` mechanism at least as cleanly as a palette PNG's
`tRNS` does — more simply, even, since it's always exactly one binary index, never a partial-alpha
entry the way a PNG palette entry can be. But that mapping only pays for itself once GIF's *color*
data also passes through byte-for-byte: today a GIF's inherently-binary transparency already gets
an efficient 1-bit stencil `/Mask` built from the decoded alpha in `ReadTrueColorMemoryBitmap`, so
a color-key array on top of an already-fully-decoded source would win nothing on its own.

Real GIF pass-through is a bigger, separate problem than reusing the mask mechanism. PDF does have
a native `/LZWDecode` filter, so in principle GIF's LZW-compressed data could pass through
unmodified the way PNG's `IDAT` (already a valid zlib stream) does — but GIF's LZW stream is
wrapped in 255-byte-max sub-blocks with GIF-specific framing, not the continuous bitstream PDF's
`/LZWDecode` expects, and GIF's/PDF's early-change-code convention needs confirming rather than
assumed. Closing this needs PeachImage to expose a GIF frame's raw LZW data with that block framing
already stripped, plus its palette and transparent-index declaration, and PeachPDF-side work to
reframe that into a PDF `/LZWDecode` stream with a color-key `/Mask`.

Tracked as [issue #1110](https://github.com/jhaygood86/PeachPDF/issues/1110).
