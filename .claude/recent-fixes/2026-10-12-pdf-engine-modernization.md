# PDF engine: `PdfSharpCore` became `PeachPDF.PDF`, on PeachDrawing.Core types, with the legacy shapes removed

The embedded PDFsharp fork moved from `src/PeachPDF/PdfSharpCore` (namespace `PeachPDF.PdfSharpCore.*`) to
`src/PeachPDF/PDF` (`PeachPDF.PDF.*`), lost its parallel drawing types, its `X*` names, its `PSSR` message class and a
large amount of dead code, and was modernized. The rules that came out of it are in
[the engine invariant](../invariants/pdf-engine-takes-peachdrawing-core-types-directly.md); this entry is the
reasoning and the traps.

**Method.** Every step was checked by rendering the 218 TestHarness showcases and hashing each PDF's decompressed
objects and streams against a baseline taken before the step (dates, font subset tags, `/ID`, `/NM`, `/M` stripped;
two runs of one build give identical hashes). Almost every step was byte-identical. Three were not, on purpose, and
each was re-baselined only after checking that the difference was what the step intended: float matrices
(61 PDFs, at most 0.0003 pt), path emission from Core's contours (116 PDFs differ in text, none rasterizes
differently in PDFium/MuPDF at 96 and 200 dpi), and the ToUnicode CMap (line feeds on every platform: it used
`WriteLine`, so Windows wrote CRLF).

**Defects found by running rather than reading.**
- `PdfImage.ReadTrueColorMemoryBitmap`'s header guard was a no-op through an operator-precedence mistake; the
  reader is now `ReadBgraBitmap` with explicit checks ([invariant](../invariants/pdfimage-bgra-reader-expects-the-bmp-shape-the-image-source-writes.md)).
- `PdfType0Font.Keys.Meta` reset its own cache and returned null; every `Keys.Meta` is now a one-line static property.
- `PdfPage.HasAnnotations` threw a `NullReferenceException` on a page with no `/Annots`; `PdfInteger`'s
  `IFormattable.ToString` returned the format string instead of the number; `GetEnumFromName(create: true)` always
  threw. All three had tests pinning the bug; the tests now pin the fixed behavior.
- A file-local `#define ITALIC_SIMULATION` made a branch that looked dead the live one; strip a conditional only
  after grepping for the symbol's `#define`.

**Not done, deliberately.** SIMD for the BGRA split: measured 14 ms against about 10 ms of Flate for the same 4 MP
image (Flate on real photographs is far slower), below the 10% end-to-end bar. Date handling (`DateTime.Now`,
`PdfDate`), the four custom encodings (`RawBigEndianUnicodeEncoding` must keep lone surrogates, which are glyph ids),
and XMP generation through an `XmlWriter` stay as they were. The native-AOT publish of the CLI compiled with no trim or
AOT warnings but could not link on the machine used (no Visual Studio C++ tools), so the runtime check is still
owed by CI.

**Evidence.** Solution rebuild 0 warnings; `PeachPDF.Tests` (net8.0, Release) and the Core, Drawing, Text and source
generator suites pass; 218 of 218 showcase PDFs match the final baseline.
