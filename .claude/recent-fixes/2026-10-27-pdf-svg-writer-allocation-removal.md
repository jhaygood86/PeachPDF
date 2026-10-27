# PDF writer and SVG backend: per-glyph / per-number allocation removal

Load-bearing idea: every change here is byte-identical output with fewer allocations; each has an
equality test against the old formulation.

- `PdfGraphicsRenderer.Text.cs` `DrawPositionedGlyphs`/`DrawGlyphsAtPositions` wrote each glyph as
  `ToString` -> `GetBytes` -> `FormatStringLiteral` -> `GetString` (about five allocations per glyph).
  They now append `<XXXX>` through `FormatHexCodeUnit`, as `DrawInvisibleGlyph` already did.
  `AllocationFreeWritersTests` checks all 65536 code units against the old chain.
- `PdfToUnicodeMap` appends hex straight into the one `StringBuilder` and encodes the BOM + text into a
  single array chunk by chunk (the CMap is pure ASCII). `/W` in `PdfType0Font` uses an interpolated
  `Append` (no boxing; invariant culture).
- `SvgDefs.Num`/`Factor` format with `TryFormat` into a stackalloc buffer; `AppendNum` lets path data
  and polygon points append without a string per coordinate. `-0`, NaN/Infinity -> `"0"` unchanged.
- Tier 3: `PdfFlateEncoder` presizes its `MemoryStream`; PNG CRC is slicing-by-4; `PdfDecryptor.ObjectKey`
  uses stackalloc + `MD5.HashData`; `Woff2Converter` copies instruction/argument bytes in one `Write`
  (`CopyBytes` keeps throwing `IndexOutOfRangeException` on a truncated table, as the byte loop did).

Not done: no BenchmarkDotNet case was added and no timing was measured, so the wins are by
allocation count only. The CFF `WriteOffset` per-byte loop was left (offSize <= 4, not hot).
