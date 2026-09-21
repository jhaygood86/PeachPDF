# `PdfGenerateConfig.Attachments`: embedded files in PDF/A-3 (and plain PDF) documents

PeachPDF already had `PdfFileSpecification`/`PdfEmbeddedFile` and a catalog `/AF` array, but only the MathML
path used them and nothing public could attach a file - `docs/usage-examples.md` said so outright. This adds
the public API (`PdfAttachment`, `PdfAttachmentRelationship`, `PdfGenerateConfig.Attachments`) and the pieces
that were missing underneath it: `/UF` and `/Desc` on the file specification, `/Params /ModDate` on the stream,
and a `/Names /EmbeddedFiles` name tree. It is the foundation the Factur-X work builds on - see
[2026-09-21-zugferd-factur-x-e-invoices.md](2026-09-21-zugferd-factur-x-e-invoices.md).

## The load-bearing ideas

**Rendering re-enters `RenderPagesCore` more often than "once per call".** It runs once per `AddPdfPages` call
*and once per `IDocumentBuilder.Page(...)`* of a declarative document, so a single `CreateDocument` with a
two-page invoice already re-enters it. Whole-document work therefore has to be idempotent. `PdfEmbeddingPlan`
freezes the validated request; `EstablishDocumentOptions` compares it against what an earlier call established
(a different set throws, in the same shape as a changed `PdfAConformance`), and `RenderPagesCore` writes it once
(`PdfDocumentOptions.EmbeddingApplied`). The plan holds *copies* of the caller's `PdfAttachment` objects, so an
edit after the first call cannot change what the document already promised.

**Use `PdfReference`, never a literal carrying an object number.** `PdfDocument.PrepareForSave` runs
`Compact()` then `Renumber()`, so a `"{0} 0 R"` literal (which `AddNamedDestination` uses) goes stale. The name
tree stores `fileSpecification.Reference`; a saved-bytes test asserts the file spec reachable from `/AF` is the
*same object number* as the one in the tree - a token-presence check would pass with the two disagreeing.

**One flat leaf is a complete name tree.** `PdfNameTreeNode.AddName` sorts and stores keys, but nothing ever
dispatches `PrepareForSave` to it, so `/Limits` is never written. A single node with `/Names` and no `/Kids` is
valid at any size and needs none, so the embedded-files tree is deliberately one flat leaf. Its keys are raw
8-bit strings (`PdfString` throws above U+00FF), which is why file names are limited to Latin-1: `/F` is the
ASCII fallback (`_` for anything else) and `/UF` (UTF-16BE) carries the real name.

**The stream is compressed at write time, opt-in per object.** `PdfEmbeddedFile.CompressOnWrite` (set from
`Options.CompressContentStreams`) reuses `PdfContent`'s write-time Flate pattern; `/Params /Size` stays the
uncompressed size. It is off by default on the class so the MathML path's output is unchanged. `/CheckSum` is
deliberately not written (optional; MD5 is a WASM/FIPS hazard).

**Fixed on the way:** both classes' `Keys.Meta` getter ended `return Keys.meta = null!;`, so it always returned
null. Now `meta ??= CreateMeta(...)`, with a test.

## Evidence

- New tests: `PdfAttachmentIntegrationTests` (saved-bytes structure, Flate round trip, ordering, non-ASCII names
  and descriptions, PDF/A level gating, validation, multi-call and declarative two-page idempotence),
  `PdfEmbeddedFilesTests` (unit), and the shared `TestSupport/PdfObjectReader` (a minimal reference-following
  reader for the write-only PDF core).
- `pdf_a3_attachments` showcase, validated with Mustang 2.26.0's embedded veraPDF: `flavour=3b`,
  1874 assertions, `isCompliant=true`. Attachments read back by both PyMuPDF (`embfile_names`) and PDFium
  (`count_attachments`).
- Debug builds fail veraPDF §6.1.9 (see the 2026-09-06 PDF/A note) - validate `-c Release` output only.
