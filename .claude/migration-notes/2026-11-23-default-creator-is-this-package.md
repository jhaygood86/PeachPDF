# A document with no Creator now says PeachPDF, not PDFsharp

**Before:** a PDF written with `PeachDrawing.PDF`'s `PdfComposer` that named no `PdfComposerOptions.Creator` had its Info `/Creator` set at
save time to `PDFsharp 1.50.4000-netstandard (https://github.com/ststeiger/PdfSharpCore)`, and a PDF/A-1b file's XMP had no `xmp:CreatorTool`,
so it failed ISO 19005-1 6.7.3.

**Now:** the default is `PeachPDF {version}` (the product name; the same string `/Producer` uses, and what the `/Producer` of a `PdfComposer` document says by default now, where it said `PeachDrawing.PDF {version}` before), written when the options are applied, so a PDF/A file's
XMP `CreatorTool` equals its Info `/Creator` and the file passes. Documents made through PeachPDF (HTML to PDF) are unchanged: they already said
`PeachPDF {version}`. Code that matched `PeachDrawing.PDF` in a `/Producer` must look for `PeachPDF`. Code that matched the old string (a metadata check, a file-type sniffer) must look for the new one; a document that names its
own `Creator` is unaffected.
