# Same-space CMYK color-mix()/gradients now work; PDF/X output now uses a correct version and identification; the declarative API now enforces conformance it claims

A `color-mix()` between two `device-cmyk()`-authored colors, or a gradient (`linear-gradient`/`radial-gradient`/
`conic-gradient`, or SVG `<linearGradient>`/`<radialGradient>`) whose stops are all `device-cmyk()`, used to
be rejected outright (an invalid `color-mix()` declaration, or a thrown `NotSupportedException` for a
gradient). Both now work, interpolating directly in C/M/Y/K component space - the same way an all-RGB
`color-mix()`/gradient already interpolated in RGB space. A `color-mix()` or gradient mixing a
`device-cmyk()` operand with an RGB-authored one is unaffected and still has no defined result.

A `PdfXConformance` document (X1a/X3/X4) used to always keep PeachPDF's default `%PDF-1.7` header and carry
no `GTS_PDFXVersion`/`GTS_PDFXConformance` identification at all (neither in the document information
dictionary nor in its XMP metadata). It now targets the correct base PDF version per level (X1a/X3: PDF
1.4; X4: PDF 1.6) and carries real identification in both the Info dictionary and XMP - a document a print
vendor's preflight tooling inspects will now actually identify itself as the PDF/X level it claims, rather
than looking like a plain, unmarked PDF 1.7 file with only an `/OutputIntents` entry.

**Breaking change, but a correctness fix**: `PdfGenerator.CreateDocument`/`AddPages` (the declarative,
code-first document-building API) previously ignored `PdfGenerateConfig.PdfXConformance`/`PdfAConformance`/
`ColorOptions` entirely for enforcement purposes - it still wrote a fully-formed `/OutputIntents`/XMP
conformance claim into the file, but never actually rejected content that violated the claimed level (a
chromatic RGB color under PDF/X-1a, live transparency under PDF/A-1, etc.), producing a PDF that falsely
claimed conformance. It now enforces exactly what the HTML-based API (`GeneratePdf`/`AddPdfPages`) already
enforced. A caller using `CreateDocument`/`AddPages` with a conformance level *and* content that actually
violates it will now get the same exception the HTML path already throws for that case, where it previously
succeeded silently.
