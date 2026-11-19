# Lab spaces: palette bytes and image samples span Lab's own ranges

PDFium's `shade` (an Indexed space over Lab used by the last column of mesh and function shadings) drew black. Indexed lookups and image
samples were always read as 0..1 per component, which is right for the device spaces and wrong for Lab, whose stored value spans
L 0..100 and a, b over the space's `/Range` (PDF 8.9.5.2 and 8.6.6.3). `PdfColorSpace.DefaultDecode` (with `DecodeMin`/`DecodeMax`)
now carries the ranges; Lab sets it, `Indexed` reads its palette through the base's, and `PdfImageXObject` uses it when an image has no `/Decode`.
An explicit `/Decode` still wins. Tests: an Indexed-over-Lab palette (two entries) and an 8-bit Lab image, all three fail without the change.

Corpus (exact fetch): 146 of 207, `shade` passes. Floor raised to 140 (Windows measured 146; Linux fonts not measured).
