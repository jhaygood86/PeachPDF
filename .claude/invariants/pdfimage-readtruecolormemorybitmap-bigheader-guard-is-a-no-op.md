# `PdfImage.ReadTrueColorMemoryBitmap`'s format-validation guard is a no-op whenever `bigHeader` is true

`ReadTrueColorMemoryBitmap` (`src/PeachPDF/PdfSharpCore/Pdf.Advanced/PdfImage.cs`) hand-parses the BMP
bytes an `IImageSource.SaveAsPdfBitmap` implementation produced, before it re-derives a PDF image
XObject's raw pixel/mask data from them. Its guard against an unexpected header shape is written as:

```csharp
if (ReadWord(imageBits, 26) != 1 ||
  (!hasAlpha && ReadWord(imageBits, bigHeader ? 30 : 28) != components * bits ||
   hasAlpha && ReadWord(imageBits, bigHeader ? 30 : 28) != (components + 1) * bits) ||
  bigHeader ? ReadWord(imageBits, 32) != 0 : ReadDWord(imageBits, 30) != 0)
```

C#'s `?:` binds *looser* than `||`, so this parses as `(A || B || bigHeader) ? C : D`, not the
presumably-intended `A || B || (bigHeader ? C : D)`. Whenever `bigHeader` is `true` (a 108-byte
`BITMAPV4HEADER`), the left side of the `?:` is trivially `true` regardless of `A`/`B`, so the whole
condition collapses to just `C` = `ReadWord(imageBits, 32) != 0` - the bitcount/compression checks
(`A`, `B`) never actually run for a `bigHeader` BMP. `PeachImageSource.SaveAsPdfBitmap` (net10.0)
produces exactly this header shape for its `Rgba32` output (`PeachImage.Formats.Bmp.Encoding.
BmpImageEncoder.EncodeRgba32`/`WriteV4Header`: 108-byte header, `BI_BITFIELDS` compression = 3). It
currently decodes correctly only because byte offset 32 happens to land on the *upper* 16 bits of the
4-byte little-endian `biCompression` field, which are `0` for any compression code under 65536 - not
because the guard actually validated the header shape.

**What this means for a future change:** if PeachImage's BMP encoder ever changes its `Rgba32` output
shape (a different header version, a compression code ≥ 65536, reordered fields), or any other
`IImageSource` implementation starts feeding `ReadTrueColorMemoryBitmap` a `bigHeader` BMP with a
different byte layout, this guard will not catch it - the code will either throw
`NotImplementedException` deep inside `PdfImage`'s constructor (aborting the whole document render, not
just skipping one image) or, worse, silently read pixel data from the wrong offsets. This is
pre-existing PdfSharpCore code, not something the PeachImage port introduced or is in scope to fix -
noted here so a future change to either side of this interaction (the BMP-writing `IImageSource`, or
`ReadTrueColorMemoryBitmap` itself) accounts for it rather than assuming the current guard means
anything.
