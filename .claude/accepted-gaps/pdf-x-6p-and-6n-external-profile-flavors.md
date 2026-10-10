# PDF/X-6p and PDF/X-6n are not offered; only complete-exchange PDF/X-6

`PdfXConformance.X6` writes a complete-exchange PDF/X-6 file (ISO 15930-9): the output-intent ICC profile is embedded.
PDF/X-6p (external gray/RGB/CMYK profile) and PDF/X-6n (external n-colorant profile) are not supported.

## Why

Both flavors reference the profile instead of embedding it, through `/DestOutputProfileRef` (a PDF 2.0 key that PDF/A-4
forbids), and 6n additionally needs n-colorant ICC output intents. PeachPDF's `ColorOptions.OutputIntentProfile` takes
profile bytes, and there is no way to express an external reference. A document that must not embed the profile is rare
next to the complete-exchange case, so the flavor most printers ask for is the one built.

## Unverified

The ISO 15930-9 text is paywalled. The identification string (`pdfxid:GTS_PDFXVersion` = `PDF/X-6`) was corroborated only
by an open-source validator (pdf_oxide) and PDF Association material, and nothing in PeachPDF's output has been run through
a PDF/X-6 preflight. Treat X6 output as unvalidated until it has been.
