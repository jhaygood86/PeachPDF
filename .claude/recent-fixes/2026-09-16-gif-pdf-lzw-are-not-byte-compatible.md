# GIF and PDF/TIFF LZW are not byte-compatible even at MinCodeSize == 8 (issue #1110)

PeachImage 0.4.6's `GifPassthrough` API (and its own doc comments) state that GIF's LZW stream is
byte-for-byte compatible with PDF's `/LZWDecode` filter whenever `MinCodeSize == 8` - the reasoning
being that both then start at 9-bit codes with the same Clear(256)/End(257) convention and "early
change" width growth. The first implementation here took that at face value: embed
`GifPassthroughInfo.LzwData` directly as `/LZWDecode`. It compiled, every unit and integration test
passed (all of them check PDF *structure* - `/LZWDecode` present, `/Indexed` colorspace, `/Mask`
array, raw bytes appearing verbatim - never actual rendered pixels), and it was only caught by the
step after that: rasterizing the showcase with both PDFium and MuPDF per this repo's own testing
convention. MuPDF errored outright ("out of range code encountered in lzw decode") and PDFium
produced a black box with a few correct pixels in one corner - both renderers agreeing on
*corrupted* output is exactly the kind of evidence this repo's testing docs call out as trustworthy,
and it was the only thing that caught this.

**Two separate incompatibilities, not one**, found by decoding a real libtiff-generated LZW stream
(via Python's Pillow, `compression='tiff_lzw'`) with independently-written decoders and comparing
against known pixel values - not by reading GIF/TIFF spec text, which doesn't resolve this
unambiguously either:

1. **Bit order**: GIF packs LZW codes least-significant-bit-first (confirmed against PeachImage's
   own `GifLzwBitWriter` source); PDF/TIFF packs them most-significant-bit-first (the classic Unix
   `compress` convention TIFF's LZW filter inherited, and PDF's `/LZWDecode` copies from TIFF).
2. **Code-width growth timing**: both conventions grow width from 9 to 12 bits using "early change"
   semantics, but *at different code counts*. GIF grows the moment its code table becomes full for
   the current width (`nextCode == maxCode`, confirmed against PeachImage's own `GifLzwEncoder`/
   `GifLzwDecoder` source). PDF/TIFF grows **one code earlier** (`nextCode == maxCode - 1`) -
   confirmed empirically: decoding a real libtiff stream with GIF's own trigger diverged exactly at
   the first growth boundary (511 codes in); with the one-earlier trigger it decoded perfectly. A
   small (<256-code) test fixture never reaches either boundary and can't catch this - the bug
   passed every hand-written unit/integration test until a large (4096-code, crossing two growth
   boundaries) fixture was rasterized.

**The fix**: `PdfImage.RepackGifLzwForPdf` (internal, unit-tested directly - see
`GifLzwRepackTests.cs`) parses GIF's LSB-first codes using GIF's own growth trigger to find code
boundaries, then re-emits the *same code values* MSB-first using PDF's one-earlier growth trigger.
This needs two independent width trackers sharing one `nextCode` counter, not one shared width - the
two conventions' widths differ for a stretch around each boundary even though the underlying code
values are identical. This is *not* a literal byte-for-byte copy the way PNG's `IDAT`/JPEG's
`/DCTDecode` pass-through are (see `EmbedPngPassthrough`/`EmbedJpegPassthrough`) - it's a cheap O(n)
bit-repack, genuinely cheaper than a full LZW decompress-then-recompress round trip (no dictionary
needed on the encode side, no pixel buffer), but not a zero-touch verbatim embed either.

**Verification beyond the synthetic fixtures**: a real animated GIF downloaded from Giphy (300x225,
interlaced - correctly excluded by the interlace eligibility gate) had its first frame decoded and
re-encoded non-interlaced via PeachImage's own `GifEncoder`, then rendered through the fixed
pass-through path and rasterized with both PDFium and MuPDF - pixel-identical, no errors, confirming
the fix on genuine photographic content and not just a synthetic full-palette pattern.

**Lesson for the next pass-through-shaped feature** (PNG alpha split, #1109; PNG/WebP/AVIF ICC,
#1106): a PeachImage doc comment asserting two formats are "byte-compatible under condition X" is a
claim to verify by actually rendering the result with both PDFium and MuPDF, not to trust from
reading the condition - this is the second time in this repo's history a pass-through feature's
tests all passed while the actual rendered output was wrong (see the `<mask>` 16/16-tests-passing
precedent `CLAUDE.md`'s testing conventions section already cites).

Tracked as [issue #1110](https://github.com/jhaygood86/PeachPDF/issues/1110).
