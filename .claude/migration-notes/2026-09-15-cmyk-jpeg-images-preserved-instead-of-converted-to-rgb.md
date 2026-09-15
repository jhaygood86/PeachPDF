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

A CMYK TIFF - previously silently converted to RGB the same way a CMYK JPEG was - is no longer
supported at all: it now throws `InvalidOperationException` (the same non-fatal "this image doesn't
render" behavior as an unsupported TGA/PSD/HDR file) rather than being given a lesser, ICC-less
conversion. See `docs/html-css-support.md`'s `img` row.
