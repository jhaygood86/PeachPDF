# /Matte on a soft mask

A soft-masked image from Photoshop or InDesign is stored blended with a matte color in proportion to its mask (PDF 11.6.5.3); ignoring `/Matte`
leaves a dark or light fringe where the mask is partly transparent. `ApplyAlpha` now undoes the blend per pixel (c = m + (c' - m) / alpha) before the
mask becomes the alpha channel. The matte is in the image's color space, which this code does not have at that point, so three numbers are read as RGB
and one as gray; any other count (PDFium's `matte` test has deliberately mismatched ones) is not applied, which agrees with PDFium. Corpus: 165 -> 166.
Tests: the premultiplied value is 100 with the matte step and about 50 without; both the wrong length and no matte stay at 50.

Looked at and left alone (see [the accepted gap](../accepted-gaps/pdfium-choices-on-invalid-function-palette-and-dash-input.md)): `bug_42270333`,
`image_transformer_other`, `dashed_lines`. Still differing by under 4% for reasons that are not defects: `bug_1693` (the soft left edge of an image
under a pattern, interpolation), `bug_1966` (PDFium smooths a magnified 1-pixel image), `bug_1746` (PDFium's Type 3 glyph under a half-opaque fill
is paler than the constant alpha the specification gives), the FreeText annotations (font substitution) and `bug_1314`, `bug_1321`, `bug_42270979_3`,
`bug_42270980` (font substitution and CMYK).
