# PDFium pixel-test runner

`src/harness/PeachDrawing.PDF.PdfiumRunner` renders PDFium's `testing/resources/pixel` PDFs through `PdfPageSource` and compares
each page with the PNG PDFium checked in (`<name>_expected.pdf.<page>.png`, the unsuffixed AGG reference; the `_mac`/`_win`/`_skia`
variants are other platforms' and are ignored). No native PDFium is involved: the expectations are PDFium's own committed images.

Traps found by running it:
- Most tests are `.in` templates, not PDFs (`{{object 1 0}}`, `{{streamlen}}`, `{{xref}}`, `{{include}}`). The runner carries a byte-exact
  port of `fixup_pdf_template.py` (Latin-1 so a char is a byte; CRLF to LF for `.in`; an include always ends on its own line).
- `pdfium_test` renders at scale 1 with the size truncated, so one pixel per point. Compare sizes first: a page-size mismatch is its own failure.
- An all-white render against an expectation with ink is `Blank`, never a pass, even when it is inside the pixel tolerance (small test pages).
- The tolerance (channel delta 48, 2% of pixels) is generous because PDFium's anti-aliasing and hinting differ; a pass is evidence, not proof.

First run (Windows, commit in `PDFIUM_COMMIT`): 207 tests, 136 pass, 19 blank (mostly form widgets), 52 fail, none crashed.
The committed floor is 125, below that run, because CI's Linux fonts were not measured; raise it with `--update-floors` after the first CI run.
Three failures are page-size mismatches in documents with several page trees (bug_2123, bug_335309995, bug_1484283): the importer's page
order or page-tree walk differs from PDFium's. Not investigated.
