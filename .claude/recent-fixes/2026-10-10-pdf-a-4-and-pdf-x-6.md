# PDF/A-4 (4, 4e, 4f) and PDF/X-6

`PdfAConformance.PdfA4/PdfA4E/PdfA4F` and `PdfXConformance.X6` are the two levels defined against PDF 2.0. What the open
veraPDF PDF/A-4 profile (`veraPDF-validation-profiles`, `PDF_A/4`) pinned down, and what we did about it:

- **Version.** `PdfGenerateConfig.PdfVersion` now tracks whether it was assigned (`PdfVersionExplicit`). A-4/X-6 with it
  unset pick `Pdf20`; explicit `Pdf17` + A-4/X-6 throws; explicit `Pdf20` + any earlier level still throws. This is the hook the
  later "default to PDF 2.0" change needs: an unset version can then fall back to what a legacy level requires.
- **Filters (6.1.6.2).** Only ISO 32000-2 Table 6 filters: `PdfDocumentOptions.StandardFiltersOnly` forces `UseBrotli` and
  `UseJxlPassthrough` off for A-4 and (by choice, not verified against the paywalled text) X-6.
- **Info dictionary (6.1.3).** The trailer may have no `/Info` unless there is a `PieceInfo`, and if present only `/ModDate`. We drop it at save
  (`PdfDocument.DoSave`, before `PrepareForSave`, so `Renumber` compacts the object numbers); the XMP packet is built earlier from the same values.
- **XMP (6.7.3).** `pdfaid:part` 4, `pdfaid:rev` 2020, and `pdfaid:conformance` only for E/F (a plain 4 must not have it).
- **Embedded files.** Only 4f; `PdfEmbeddingPlan` rejects them for 4 and 4e (it would otherwise have fallen through the A1/A2 list and allowed them). veraPDF 6.9 test 5 also *requires* an `EmbeddedFiles` entry for 4f, so 4f with no attachment throws - found only by running the validator.
- **PDF/X-6** identifies itself only as `pdfxid:GTS_PDFXVersion` = `PDF/X-6` in XMP, with no Info GTS keys and no legacy `pdfx:` block.
- Unchanged and already right for A-4: always-embedded subset fonts with ToUnicode, `GTS_PDFA1` sRGB output intent, no `Interpolate`, no
  transparency restriction (the guard only lists A-1 / X-1a / X-3), tagging not forced (the `isAccessibleConformance` list names only the "a" levels).
- **Noticed, not changed:** the existing X-4 path still writes `/GTS_PDFXVersion` into the Info dictionary; sources found while researching say
  PDF/X-4 wants it only in XMP. Not verified against ISO 15930-7.
- **Validated** with veraPDF 1.30.3 (CLI, `--flavour 4|4e|4f`) on CLI output with text, opacity, gradient, SVG, table, PNG, form and link content: PASS for all
  three once built in Release. A **Debug** build FAILS 6.1.8 (36 checks): every object gets a `% PeachPDF...` comment after `obj`, which is
  not an EOL. Always validate Release output. Negative controls: the same file against `2b`, and a plain PDF 2.0 + Brotli file against `4`, both FAIL
  (veraPDF reports `Unknown decode filter /BrotliDecode`).
- **Not done:** no PDF/X validator exists (see the accepted-gaps note for X-6); X-6p/6n; Factur-X on 4f.

## CI conformance job and the PDF/A-1 /CIDSet it found
`test.yml`'s `conformance` job runs `.github/scripts/Test-PdfConformance.ps1`: `.github/conformance/sample.html` at all 11 PDF/A levels through the
Release CLI, each validated by veraPDF (1.30.3, SHA-256-pinned, from Maven Central; installed headless with an izpack auto-install XML, which needs the
"Mac and *nix Scripts" pack on Linux for the `verapdf` launcher). Its first run found that **PDF/A-1a/1b failed rule 6.3.5**: ISO 19005-1 requires a
`/CIDSet` bitmap on every CIDFont subset descriptor and PeachPDF never wrote one. `PdfCIDFont.PrepareForSave` now writes it for PDF/A-1 only (CID == glyph
index, glyph 0 always present; later parts made it optional, so they skip the object). The script is Windows-PowerShell-5.1 compatible so it can be run
locally: build the CLI in Release and pass `-VeraPdf` / `-CliDll`. The Linux install steps in the workflow were not run before the PR (only the script, on Windows).
