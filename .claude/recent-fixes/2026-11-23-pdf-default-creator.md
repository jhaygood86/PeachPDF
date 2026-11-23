# The default Creator is this package, in the Info and the XMP alike

A PDF/A-1b file the composer wrote failed ISO 19005-1 6.7.3 (veraPDF: "the value of Creator ... and its analogous XMP property
`xmp:CreatorTool` shall be equivalent") unless `PdfComposerOptions.Creator` was set, signed or not; found while validating signed files
([the validator note](2026-11-23-pdf-signing-validator-and-vri.md)). It had a second, visible symptom: a document that named no creator
said `PDFsharp 1.50.4000-netstandard (https://github.com/ststeiger/PdfSharpCore)`, the engine's inherited identification, in its Info.

**Cause:** two defaults in two places, one wrong. `PdfDocument.PrepareForSave` filled a missing Info `/Creator` with
`PdfProductInfo.Producer` (the PDFsharp string) *at save time*, but the XMP packet of a PDF/A file is written from the Info *when the options
are applied*, when `/Creator` was still empty, so it got no `CreatorTool`. PDF/A-1 wants the two equal when both exist; the later parts relax it,
which is why 2b, 3b and 4 passed.

**Fix:** `PdfProductInfo.Producer` is now `PdfComposerProductInfo.Generator` (`PeachPDF {version}`: the product name, and the one string `/Producer` also uses),
and `ApplyInformation` gives a document with no creator that default *when the options are applied*, so the XMP built from the Info sees it: one
source, one value, both places. A named `Creator` still wins, and applying options again keeps the default unless a creator is named. The
first version of this fix said `PeachDrawing.PDF {version}` (the package's own name, which `/Producer` had used since October); the product is PeachPDF,
so the one generated string now says `PeachPDF {version}` and **`/Producer` changed with it**: the composer's default Producer was
`PeachDrawing.PDF {version}` before (see [the October entry](2026-10-19-pdf-writer-public-api.md)). Documents made through the PeachPDF
library already said `PeachPDF {version}` (`PdfGenerator` names both fields itself), unchanged. The engine's unused `PdfProductInfo.Version`
constant went with the old string.

**Evidence:** `PdfCreatorTests` (8): the default is `PeachPDF` and `x.y.z` and equals the producer, no `PDFsharp`/`PdfSharpCore` anywhere in the
bytes (also as hex UTF-16), a named creator is kept in Info and XMP, PDF/A-1b/2b/3b/4 with no creator carry an equal `CreatorTool`, applying options
twice. veraPDF 1.30.4 passes 12 PDF/A files with **no** `Creator` set (unsigned, composer-signed, signed in place at 1b, 2b, 3b and 4). No pinned
baseline moved: the corpus names its creator.

**Not done:** a document written by PeachPDF with PDF/A-1b and an HTML `<meta name="generator">` that differs from its Producer is as before
(its Info and XMP agree because both come from the one value PeachPDF passes).
