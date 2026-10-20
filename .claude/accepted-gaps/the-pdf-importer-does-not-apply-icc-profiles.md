# The PDF importer does not apply ICC profiles

`PdfPageSource` (`src/PeachDrawing.PDF/Import/`) reads an `ICCBased` colour space by its component count, as gray, RGB or CMYK
(`PdfColorSpace.Resolve`), and falls back to `/Alternate` for any other count. The embedded profile itself is never read, so a
page whose colours depend on a non-sRGB profile (a wide-gamut image, a CMYK press profile) renders differently from the original in
a colour-managed viewer. Tracked in issue 1866.

**Measured:** the showcase round trip (`--import-roundtrip` and `compare-roundtrip.py`) differs on `icc_profile_preservation.pdf` by
about 3% of pixels in both MuPDF and PDFium (the two agree with each other), the only file left whose difference is explained by this.

**Why it is not done:** it needs an ICC engine. Matrix/TRC profiles (the common RGB and gray ones) are small to implement; the
LUT-based CMYK profiles are not. The writer already embeds sRGB and CMYK profiles for PDF/A output, which is the likely starting point.

**What a fix has to keep:** a colour read through a profile must still reach a `PdfCanvas` as the same ICC-based space where possible
(the canvas has `PaintColor.FromCmyk` for native CMYK), not be flattened to RGB, or the output loses the colour intent the source carried.
