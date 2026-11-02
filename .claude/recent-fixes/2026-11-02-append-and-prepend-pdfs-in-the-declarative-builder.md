# `IDocumentBuilder.Append` / `Prepend`: existing PDFs written into the generated document

**What it is.** A PDF section is a builder section like a `Page` call: `DocumentBuilder.Sections` is one ordered list
(page handler | `PdfOperation` + optional `PdfCopyMode`), `AddPagesCore` walks it, and a PDF section calls the new public
`PdfOperation.AppendTo(composer, context, mode)`. The pages are drawn (or object-copied) into the generated document's *own*
composer, so its PDF/A, encryption, tagging, `Linearize` and metadata cover the whole file. The alternative considered and
rejected was saving the generated file and running a second `PdfOperation` pass, which re-reads the file and loses
conformance and encryption.

**`AppendTo` is the writer, split.** `PdfOperationWriter.Write` is now "create composer, `Append`, save". `Append` offsets
every page index by `composer.PageCount`, and the sets of names it hands out (merged destination names, merged field names)
live in a `ConditionalWeakTable<PdfComposer, AppendState>` so two calls into one composer never collide. The generated
link-target names carry the call ordinal from the second call on (`PeachPDFMerged1_0-1-10`): without it the second file's
link to *its* page 2 reused the first file's name and silently led to the wrong page. Document-level steps (encrypt,
linearize, attachments, metadata) throw `InvalidOperationException`, because they belong to the file being written.

**The trap: `Composer.Configure` must come before the first page.** `RenderPagesCore` configures the composer from the first
generated section (PDF/A needs its title, language and date). With a PDF section first, pages would be added to an
unconfigured composer, and `AddPage` configures with defaults, after which a PDF/A request is a conflict. So `AddPagesCore`
lays out the first generated section ahead of time when a PDF precedes it and calls the extracted `ConfigureDocument` (which
takes a null `HtmlDocument` for a document of only PDFs and then uses `PdfGenerateConfig.DefaultLanguage`). `Configure`
being repeatable is what makes the second call from `RenderPagesCore` harmless.

**A bug it uncovered.** `RenderPagesCore` handed the document the outline entries and form fields with page indices that
are local to the section being rendered. For the first section that is right; for any later `Page` call (and for any section
after an added PDF) the outline entry and the widget landed on the wrong page. Both now add `pageBase`
(`composer.Pages.Count` before the section's pages). `TwoPageSections_PutTheOutlineOfTheSecondOnItsOwnPage` covers the
plain two-`Page` case.

**Numbering.** A section is numbered by its place among all sections, so `CountPages` returns one count per section and the
merge arithmetic (`partBefore + within`, grand total) is unchanged. A PDF section counts for the numbers only when the
builder called `UseContinuousPageNumbers()` or the merge is `Continuous`; otherwise it counts 0 and the generated pages
number from 1 as before.

**Evidence.** `PdfOperationAppendTests` (9) and `DocumentBuilderPdfSectionTests` (14) pass; the showcase `document_append_pdf`
was rasterized with PDFium and MuPDF; a linearized, PDF/A-2b, encrypted bundle was checked with qpdf.
