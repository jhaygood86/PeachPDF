# Factur-X: PDF/A-3 only - the optional PDF/A-4f container is not offered

The Factur-X 1.09.2 specification allows, optionally, a PDF/A-4f file (ISO 19005-4, PDF 2.0) as the container
in place of PDF/A-3. `PdfGenerateConfig.FacturX` requires one of `PdfA3B`/`PdfA3U`/`PdfA3A`.

## Why

PeachPDF implements no PDF/A-4 level at all (`PdfAConformance` stops at part 3, and
`PdfGenerator.EstablishDocumentOptions` rejects `PdfVersion.Pdf20` together with any PDF/A level). Supporting
the 4f container would mean building PDF/A-4 generally - a different XMP identification (`pdfaid:part` 4 with
the `F` revision), PDF 2.0 based rules, and its own validation - not a Factur-X-specific addition. PDF/A-3 is the
container the specification names first, every validator and receiving system accepts, and the one the German
and French rollouts use.
