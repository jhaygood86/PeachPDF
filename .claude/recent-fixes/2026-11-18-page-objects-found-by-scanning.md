# Page objects found by scanning when the catalog or page tree is lost

pdf.js `issue9418` (a catalog with no /Pages; pdf.js corpus 777 -> 778) threw `The PDF has no page tree`, though PDFium renders it. After a repair has been
tried, `CollectPages` now falls back to `ScanForPages`: every object with `/Type /Page`, in object-number order, as a viewer's rebuild does, for a missing
catalog, a catalog with no /Pages, or one naming a tree that is absent. Where no page object survives the original error stands.

Not helped: `Brotli-Prototype-FileA` (PDF 2.0 Brotli-compressed object streams, a filter this reader does not have), `REDHAT-1531897-0` (nothing page-like left)
and `PDFBOX-4352-0` (an unreadable encryption dictionary). `poppler-395-0-fuzzed` now opens but is skipped by the runner (PDFium has no reference for it).
