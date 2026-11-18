# PDF import: predefined and embedded CMaps for CJK Type0 fonts

`PdfImportedFont` used to accept only `Identity-H/V`; any other `/Encoding` on a Type0 font (embedded or not) was reported unsupported and its text lost.

- `Import/CMaps/` holds `PdfCMap` (text parser, per-spec variable-length code splitting, `usecmap` parents, WMode) and `PdfPredefinedCMaps`: 59 Adobe CMaps plus CID->Unicode tables for the four collections in one Deflate resource (`PdfCMaps.bin`, ~385 KB). Deflate, not Brotli: the text engine's Brotli seam is internal and WebAssembly has no `BrotliStream`.
- Regenerate with `PEACH_REGENERATE_CMAPS=<folder of Adobe files> dotnet test ... --filter-method "*Regenerate*"`. The inversion of `Uni*-UTF16-H` must prefer ordinary characters over Kangxi radicals and compatibility ideographs (the first version gave U+2F42 for the CNS1 CID of U+6587).
- Text of a code: ToUnicode (keyed by *code*, not CID) -> collection table -> ASCII for a one-byte code 0x20-0x7E (legacy CMaps keep Latin as one byte on half-width CIDs the Unicode CMaps never map).
- Vertical writing (`ShowVertical`): glyph centered on the pen with the default position vector (0.88 em), pen advances down one em; read text reports downward lines. `/W2`/`/DW2` are not read.
- Non-embedded CJK fonts are drawn with an installed family chosen by the collection (`PdfFontSubstitution.CjkFamilies`); glyphs are found by Unicode.
- Not done: per-glyph vertical metrics, CID->Unicode for Identity-ordered fonts without ToUnicode.
