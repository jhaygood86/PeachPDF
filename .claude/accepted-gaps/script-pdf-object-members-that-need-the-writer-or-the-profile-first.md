# PDF object members that are not provided

The `PDF` script object (PeachPDF, see docs/peachbrackets-scripting.md) covers the file's information, how a viewer opens it, encryption, attachments
and `Prince.failStatus`. The members other print engines give a script that PeachPDF does not:

- **`profile`, `outputIntent`, `colorConversion`, `colorOptions`, `fallbackCMYKProfile`**: the PDF/A, PDF/X and PDF/UA level and the color setup are
  decided in `PdfGenerateConfig` before the scripts run, and they change how the document is built (tagging, embedded profiles, what the
  conformance check refuses). A script cannot reach back and change that, and setting it late would produce a half-configured file. Use
  `PdfGenerateConfig`.
- **`embedFonts`, `subsetFonts`, `artificialFonts`**: the writer always embeds and subsets fonts (`PdfFont.CreateEmbeddedFontSubsetName`); there is
  no switch, and a file with non-embedded fonts is not valid PDF/A. Adding one is writer work, not scripting work.
- **`objectStreams`**: the writer does not write object streams or cross-reference streams at all (only the importer reads them), so there is
  nothing to turn off.
- **`keyBits` other than 256**: encryption is AES-256 only (RC4 and AES-128 are deliberately not offered); assigning another value is an error
  rather than being silently ignored.
- **`paperTray`, `filterResolution`, `forceIdentityEncoding`, `allowAssembly`, `allowCopyForAccessibility`**: no option to map them onto yet
  (`PdfPermissions` has `AllowAssemble` and `AllowAccessibilityExtract`, which `allowModify` and `allowCopy` currently drive together).

Not a spec deviation, so no issue is filed; file one per member if a document needs it.
