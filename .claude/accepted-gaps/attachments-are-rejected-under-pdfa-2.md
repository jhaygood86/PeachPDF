# Attachments are rejected under PDF/A-1 and PDF/A-2 - PDF/A-2's "only PDF/A files" allowance is not used

`PdfGenerateConfig.Attachments` throws when a `PdfA1*` or `PdfA2*` level is requested. That is exact for
PDF/A-1 (embedded files are forbidden outright), but PDF/A-2 (ISO 19005-2 §6.8) does allow embedding - as long
as each embedded file is itself a conforming PDF/A file.

## Why

Whether an attachment is a conforming PDF/A file cannot be established in-process: PeachPDF has no PDF/A
*reader* (the PDF core is write-only, per the PdfSharpCore notes in `CLAUDE.md`) and no validator. Allowing "any
`application/pdf` attachment under PDF/A-2" would let a caller produce a non-conforming file with no diagnostic,
which the PDF/A work in this repo consistently refuses to do (transparency under PDF/A-1, a missing language
under the "A" levels, a missing creation date). Rejecting and pointing at `PdfA3*` costs a caller nothing they
could not already do: every use of attachments in practice wants PDF/A-3.

Revisit if a PDF/A file detector (an XMP `pdfaid` read plus a validation pass) ever lands.
