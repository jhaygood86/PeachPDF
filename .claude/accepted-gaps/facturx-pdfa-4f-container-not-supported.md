# Factur-X: PDF/A-3 only - the optional PDF/A-4f container is not offered

The Factur-X 1.09.2 specification allows, optionally, a PDF/A-4f file (ISO 19005-4, PDF 2.0) as the container
in place of PDF/A-3. `PdfGenerateConfig.FacturX` requires one of `PdfA3B`/`PdfA3U`/`PdfA3A`.

## Why

`PdfAConformance.PdfA4F` exists now (plain attachments work with it), but `FacturXInvoice`/`PdfEmbeddingPlan`
still require a PDF/A-3 level: the Factur-X XMP extension schema and the `fx:` properties have only been checked against
a PDF/A-3 container, and a 4f Factur-X file needs its own validation (the XMP `pdfaid:rev`, no Info dictionary to
mirror). PDF/A-3 is the container the specification names first, every validator and receiving system accepts, and the
one the German and French rollouts use.
