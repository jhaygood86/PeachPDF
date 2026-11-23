# A PDF/A-1b file the composer writes fails ISO 19005-1 6.7.3 unless `PdfComposerOptions.Creator` is set

Found by running veraPDF over signed PDF/A files, not by the signing work: an *unsigned* PDF/A-1b file written by `PdfComposer` fails
rule 6.7.3 (test 6): the Info dictionary's `/Creator` is a default string the engine inherited from PDFsharp
(`PDFsharp 1.50.4000-... (https://github.com/...)`), while the XMP packet has no `xmp:CreatorTool`, because the XMP is built from the Info
before the engine sets that default. ISO 19005-1 requires the two to be equivalent when both are present; later parts relax it, so PDF/A-2b,
3b and 4 pass.

Setting `PdfComposerOptions.Creator` to any value makes the Info entry and the XMP property agree and the file pass, which is how the signed
PDF/A-1b samples were validated. Not fixed here: removing or deferring the engine's default `/Creator` changes the bytes of every file the
library writes (the output baselines pin them), and the fix belongs with the PDF/A-1 work, not the signing change. A tracking issue is still
to be filed for the spec deviation.
