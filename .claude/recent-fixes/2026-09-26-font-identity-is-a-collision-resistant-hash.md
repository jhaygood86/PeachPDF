# A font's identity is a SHA-256 hash, not an Adler-32

## What was wrong

The process-wide font source cache (`FontFactory.FontSourcesByKey`, behind `FontSet.AddData`) and the `name#suffix` face name
that `FontResolver.AddFont` gives a second, different font registered under one internal name were both keyed by
`FontFileData.CalcChecksum`: the Adler-32 of the bytes in the high word and the length in the low word. Adler-32 is a
sum, not a hash. Editing three consecutive bytes by `+1, -2, +1` (the second difference of the byte weights is zero) keeps the
sums and the length, so a font with one glyph's advance changed collides with the original and is *served the original's parsed
data*. The cache is shared by every `PdfGenerator` in the process and fonts arrive from `@font-face` in untrusted HTML, so a
crafted font could displace another document's. `PdfFontTable.ComputeKey` and `RFont.FaceKey` (through the public
`Typeface.ContentHash`) also decided which fonts share one embedded copy by it. It surfaced because the CFF2 hinting tests that
damage a font by a byte had to use `FontFileData.CreateCompiledFont` to stay out of the cache.

## The change

- `FontContentHash` (`Internal/Fonts/FontContentHash.cs`) is the identity: the first 128 bits of the SHA-256 of the bytes, an
  internal `readonly struct` (two `ulong`s; `default` means "not computed", which the lazily keyed compiled font relies on).
  `FontFileData.GetOrComputeHash` keeps the existing memo by buffer identity (`ConditionalWeakTable`), so a buffer is still hashed
  once. The dictionary, `FontFileData.Equals/GetHashCode` and the `#` face-name suffix all use it. `IncrementKey` (a `HACK`
  that depended on the checksum's layout, never called) and the dead debug byte loop in `CacheFontSource` are gone; the hit path
  now `Debug.Assert`s byte equality.
- **The BCL `SHA256` does the work**, on every TFM without a package: it is hardware accelerated, and it is not instrumented by
  coverlet. That second point matters: [the checksum was once the hottest line of the coverage run](2026-09-13-font-checksum-recomputed-on-every-lookup-even-cache-hits.md),
  and a managed hash would be ten times as many instrumented operations per byte. Where the platform has no SHA-256 (it throws
  `PlatformNotSupportedException`/`CryptographicException`, as a WebAssembly host may) `PortableSha256` computes the same digest in managed code. A test holds it to the platform digest at every padding boundary.
- **`Typeface.ContentHash` changed from `ulong` to `string`** (32 lowercase hex digits). It could not stay a `ulong`: the PDF
  writer reaches the engine only through the public API, and a 64-bit value is a birthday collision of about 2^32 hashes even
  when it is a truncated SHA-256. The type was one day old and unreleased (`PublicApi.txt` updated, the naming row in
  `text-public-api-must-not-mirror-a-competitor.md` reworded). `FontAdapter.FaceKey` and `PdfFontTable.ComputeKey` already
  concatenated it into a string key, so they needed no more than dropping `ToString("x")`.
- `OpenTypeFontface.CheckSum` (unused outside a test) is removed. **The OpenType table checksums and `checkSumAdjustment`
  are untouched**: they are a format-mandated, unrelated checksum.

## What the evidence is

- The 184 showcase PDFs of `PeachPDF.TestHarness` before and after (the base commit and this one, same machine) are
  byte-identical once the values that change on every run are masked (dates, `/ID`, the random six-letter subset tags, annotation
  GUIDs, XMP timestamps). Nothing in the PDF derives from the key: the face name is never written to a document and the subset tag is
  random, so there is no migration note.
- `FontContentHashTests`: two fonts (and a third) of one length and equal Adler-32, built by the edit above on a `hmtx` advance,
  get separate cache entries that each read their own advance, get different face names that each fetch their own bytes back
  (the old suffix was the same for both), and 64 concurrent lookups of copies of five contents yield exactly one source per content.
  The premise (equal Adler-32, same length, different bytes) is asserted by a test of its own.

## Traps

- Do not memoize the hash of a compiled font (`CreateCompiledFont`): tests and the subsetter change its bytes after making it, and
  the buffer-identity memo would return the stale hash.
- `Typeface.ContentHash` says nothing of the location of a variable font; every cache built on it still adds `VariationKey`.
