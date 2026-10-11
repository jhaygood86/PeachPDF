# PeachPDF now writes PDF 2.0 by default

**Before:** `PdfGenerateConfig.PdfVersion` defaulted to `Pdf17`, which wrote a `%PDF-1.4` file (later, `1.6`/`1.7` when a PDF/A, PDF/X or attachment
feature required it). **Now:** it defaults to `Pdf20`, so a document that sets nothing starts with `%PDF-2.0`.

- Streams are still compressed with Flate: `BrotliCompression` is an opt-in (default `false`; CLI `--brotli`), because PDFium (Chrome/Edge's viewer, pypdfium2)
  and Acrobat show Brotli streams as blank pages. See `.claude/recent-fixes/2026-10-10-brotli-decode-streams-for-pdf-20.md`.
- A PDF/A or PDF/X level requested without touching `PdfVersion` still produces what that standard defines: `PdfA1*` 1.4, `PdfA2*`/`PdfA3*` 1.7, `X1a`/`X3` 1.4,
  `X4` 1.6, and the new `PdfA4*`/`X6` 2.0. Only an explicit `PdfVersion = Pdf20` with one of the earlier levels throws, as before.
- To get the previous output, set `PdfVersion = PdfVersion.Pdf17` (CLI: `--pdf-version=1.7`).
- Tagged-PDF `Formula` structure elements now carry `/AF` in a file whose header agrees with it, without opting in.
- Confirm against `git show <previous-tag>:docs/usage-examples.md` when cutting release notes: the previous release documented `Pdf17` as the default.
