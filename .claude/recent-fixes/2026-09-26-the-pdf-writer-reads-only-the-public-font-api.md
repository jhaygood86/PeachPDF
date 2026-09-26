# The PDF writer reads only the public font API

The last step of the public-API port: PeachPDF no longer has `InternalsVisibleTo` access to `PeachDrawing.Text`, so the
compiler now enforces what the earlier slices only promised. `PublicSurfaceTests` also fails if an entry for a PeachPDF
assembly comes back. Only the test project keeps the bridge, for the engine's own tests, until they move.

## What the load-bearing idea was

Everything the PDF writer took from the engine's internals was either **a fact about a face** or **a fact about the PDF
format**. The facts about a face became public members: `TypefaceMetrics` gained `IsSymbolic`, `IsFixedPitch`, `HasSerifs`,
`IsItalicStyle` and `FirstCharIndex`, and `Typeface` gained `FullName` and `ContentHash`. The facts about the PDF format moved
into the writer: `PdfTypefaceMetrics` (thousandths of an em, the truncating glyph width, the base font name) and `CMapInfo`
(which glyphs a document used and what text they stand for), which now reads a `Typeface` and shapes through the public
`Shaper`. The one thing that had to be an engine operation, cutting a font down, is `TypefaceExporter.ExportSubset`.

## Traps

- **An embedded subset cannot be read back.** It has no name table, so `FontSet.AddData` throws `TypefaceFormatException` and
  the engine's own reader throws a `NullReferenceException`. Tests of an exported font parse `loca` from the bytes.
- **`XFont` no longer holds a `LoadedTypeface`.** What the renderer has to fake is `XFont.Synthesis`, taken from the
  `TypefaceMatch`; `Typeface.IsBold`/`IsItalic` are what the face declares and are not the same thing.
- **`RFont.FaceKey`** is now `ContentHash` plus the synthesis. It used to be the engine's typeface key, which contained the
  family name; two faces with equal data and equal synthesis are the same face for the run-splitting that reads it.
- **`PdfFontTable.ComputeKey`** now uses `FullName` and `ContentHash`. It is content-addressed on purpose (two different files
  can share an internal name), and that is preserved.
- A font with CFF outlines that reaches the simple-TrueType path used to throw a `NullReferenceException` at save; it now
  gets the whole font. The path is not reachable from HTML today.

## Evidence

Full `PeachPDF.Tests` suite in Release, and a zero-warning rebuild of the solution. The existing PDF and rasterization tests
pass unchanged, which is what shows the font dictionaries (names, widths, descriptors, embedded streams) did not move.
