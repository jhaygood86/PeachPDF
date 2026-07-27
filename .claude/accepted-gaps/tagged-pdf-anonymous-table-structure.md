# Tagged PDF: anonymous table structure cannot have its tagging overridden

Tagged PDF: anonymous (CSS-generated, e.g. `display: table-cell` on a `<div>`) table structure cannot have its `TR`/`TH`-or-`TD`/`THead`/`TBody`/`TFoot` tagging overridden via `-peachpdf-pdf-tag-type` — the synthesized anonymous boxes have no source element for any selector to match. Real `<table>`/`<tr>`/`<td>` markup is required for override control. See [Tagged PDF (PDF/UA) Support](docs/html-css-support.md#tagged-pdf-pdfua-support).
