# PeachDrawing.PDF internals refactor: a reproducible writer first, then the cleanup

The goal was to simplify the internals of `src/drawing/PeachDrawing.PDF` without changing a byte of what it writes. Nothing public changed.

**The load-bearing idea.** The tests asserted on parsed structure and substrings, so a refactor of the writer could not prove it changed nothing. `Engine/Internal/PdfEntropy.cs` is now the only place the writer asks for randomness or the time
(document and annotation IDs, font subset tags, `/M` dates, the AES-256 salts and IV). A test installs a fixed `PdfEntropy.Source` for its async flow, and
`PeachDrawing.PDF.Tests/Baselines/PdfOutputBaselineTests.cs` pins the SHA-256 of 11 documents (plain, compressed, linearized, XMP, PDF 2.0, PDF/A-1b and 2b, AES-256, AES-256 plus linearized, bookmarks, named destination). Every later step
was checked against it: all of them left the hashes unchanged.

**Found by running it.**
- The survey's "dead code" list was mostly wrong. `PdfNumberTreeNode` (structure tree `/ParentTree`), the `PdfName`/`PdfReference` comparers, `PdfProductInfo` and `PageSizeConverter` are used. Only grep-verified types were deleted.
- IDE0051 reported `PdfViewerPreferences(PdfDictionary)` as an unused private constructor. It was used, through `ConstructorInfo.Invoke`. Anything an analyzer calls unused in this project has to be checked for reflection first.
- `PdfContentWriter` and `LatinOneByteEncoding` looked like hand-rolled leftovers and are not: the writer already formats into spans, and the encoding truncates a char above 255 where `Encoding.Latin1` writes `?`.
- A global spelling sweep (`colour` to `color`) broke tests because runtime diagnostics contain `colour` in string literals that tests pin. Comments are safe to change; string literals are behavior.

**The key metadata.** `[PdfKey]`/`PdfKeyDescriptor`/`PdfDictionaryMetadata` (346 attributes, reflection, `DynamicallyAccessedMembers` in about 27 files) existed for one lookup: which class to create or wrap for a key. Every production caller already named the class
through `GetOrCreate<T>`, so `IPdfDictionaryValue<T>` / `IPdfArrayValue<T>` (static abstract `Create` and `Wrap`, implemented explicitly so they do not hide each other down the class hierarchy) replaced it. The per-key "Required/Optional/PDF x.y"
information the attributes carried was copied into each key's doc comment first.

**Save paths.** The linearizer now builds its cross-reference sections and trailers through `PdfWriter.ToBytes` and `PdfCrossReferenceTable.InUseEntry`/`FreeListHeadEntry`, the same calls the ordinary save uses. Its `/Prev` placeholder is patched in the bytes, searched only after the `/Prev` token.

**Not done.** `PdfGraphics`, `PdfContentInterpreter` and `PdfCanvas` are not split. `PdfGraphicsRenderer` got a partial-file split (`.Compositing.cs`), not a collaborator class, because those methods use its private state heavily. The Native AOT link of `PeachPDF.Cli` could not be run on the development
machine (no native toolchain); the Release build has no trim or AOT analyzer warnings. The baselines were produced on Windows; the first Linux/macOS CI run is the check that Flate output matches there.

**Evidence.** `PeachDrawing.PDF.Tests` (net10.0 Debug and Release, net11.0), `PeachPDF.Tests`, solution rebuild with zero warnings.
