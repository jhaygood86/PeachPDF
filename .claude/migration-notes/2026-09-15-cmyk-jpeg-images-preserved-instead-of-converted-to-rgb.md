# CMYK/YCCK JPEG images preserved instead of converted to RGB; embedded ICC profiles now used

A CMYK or YCCK JPEG - the form a print-ready image typically arrives in, separated for a specific press
profile - used to be silently converted to RGB (with no color management at all) and re-encoded as a
lossy JPEG before being embedded in the PDF, destroying the original separations. It's now embedded via
byte-for-byte pass-through instead: the PDF's image color space is `DeviceCMYK`, or `ICCBased` when the
source carries a usable embedded ICC profile (carried through verbatim). A document containing a CMYK
JPEG will now render with correct, unconverted CMYK color data instead of the previous RGB
approximation - a real difference in output color a document author could notice, and in the right
direction (previously-wrong colors becoming correct).

A CMYK/YCCK JPEG is also now always embedded at its natural pixel size - `DownscaleImages` and
`MaximumDownscaleMultiplier` no longer resize it, since there's no CMYK JPEG encoder to re-encode a
resized copy with. Previously it was downscaled like any other image.

Separately, an RGB or grayscale JPEG carrying a usable embedded ICC profile is now also embedded via the
same byte-for-byte pass-through (to preserve that profile, via `ICCBased` instead of the usual bare
`DeviceRGB`/`DeviceGray`) whenever it isn't being resized for its on-page display size; if it is being
resized, that specific embed falls back to the previous re-encoded behavior (without the profile) rather
than being downscale-exempt like a CMYK source. An RGB/grayscale JPEG with no embedded ICC profile is
completely unaffected.

**Breaking change**: requesting `PdfAConformance` on a document containing a CMYK image that has no
embedded ICC profile now throws an `InvalidOperationException` at generation time, where it previously
succeeded (producing a `DeviceCMYK` image with no relationship to PeachPDF's RGB-based PDF/A output
intent - not actually PDF/A-conformant, just previously ungated). A CMYK image with an embedded ICC
profile is unaffected and remains conformant. An RGB or grayscale image is unaffected either way.

A CMYK TIFF - previously silently converted to RGB the same way a CMYK JPEG was - is now decoded
natively and embedded as a raw CMYK raster (`/FlateDecode`, `DeviceCMYK` or `ICCBased` when the source
carries a usable embedded ICC profile), the same as a CMYK JPEG except via a real re-encode of the
decoded pixels rather than a byte-for-byte pass-through (TIFF has no `/DCTDecode`-equivalent filter to
pass through). Like a CMYK JPEG, it is always embedded at natural pixel size (no downscaling) and is
subject to the same `PdfAConformance`-without-an-embedded-ICC-profile restriction described above. See
`docs/html-css-support.md`'s `img` row.
