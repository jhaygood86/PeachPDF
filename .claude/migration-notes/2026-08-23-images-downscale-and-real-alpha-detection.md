# Raster images now downscale before embedding; alpha detection is now real, not a PNG sniff

**Downscaling.** Previously: every raster image (`<img>`, `background-image`, `<object>`/`<video poster>`,
list-style-image/marker, `::before`/`::after content: url(...)`, and SVG `<image>`) was embedded in the PDF
at its full decoded pixel resolution, regardless of how small it was actually displayed - a 6000x4000
source photo shown at 200x150 CSS px still shipped at full resolution, bloating output file size for no
visible benefit.

Now: `PdfGenerateConfig.DownscaleImages` (default `true`) resizes an oversized image down to (roughly) its
actual on-page display size before embedding, using PeachImage's `Image.Resize` (Bicubic filter).
`PdfGenerateConfig.MaximumDownscaleMultiplier` (default `1.0`) adds headroom above the exact display size
if set above `1.0`; `PdfGenerateConfig.DownscaleQuality` (default `70`) is the JPEG quality used
specifically for a resized, non-alpha image's own encode (full-resolution JPEG embeds are unaffected). The
same source image used at genuinely different display sizes in one document embeds once per distinct size
rather than once at whichever size was drawn first. Set `DownscaleImages = false` to restore the previous
always-full-resolution behavior.

**Alpha detection.** Previously: whether an image was embedded losslessly (preserving alpha, as an
uncompressed PDF bitmap) or as lossy JPEG was decided by sniffing the source file's first 4 bytes for the
PNG signature - not by checking whether the image actually has any real transparency. A WebP, AVIF, or GIF
image with genuine alpha was silently routed to the lossy JPEG path and lost its transparency; conversely,
a fully opaque PNG took the lossless (larger, uncompressed) path it didn't need.

Now: every decoded image is scanned for real alpha content once, regardless of source format, and that
scan result (not the source format) decides the embed path. WebP/AVIF/GIF images with genuine transparency
now render correctly; an opaque PNG now JPEG-embeds like any other opaque source, which is also smaller in
most cases. Images with real alpha are unaffected by `DownscaleImages`'s JPEG-quality behavior above - they
stay on the lossless embed path regardless of downscaling, just resized.
