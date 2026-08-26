# Raster embed path now follows the source's declared alpha channel, not a scan of real pixel transparency

Previously: whether a decoded raster image embedded losslessly (preserving alpha, as an uncompressed PDF
bitmap) or as lossy JPEG was decided by scanning every decoded pixel's alpha byte for real transparency -
an image with a declared alpha channel but no actually-transparent pixels (e.g. an RGBA-color-type PNG
where every pixel happens to have alpha=255) took the smaller JPEG path, same as a fully opaque source in
any other format.

Now: the same decision is made by PeachImage's own `Image.HasAlpha`, which reflects whether the *source
format itself declares an alpha channel* (PNG color type, WebP alpha flag, GIF transparent-index
declaration, BMP alpha mask, etc.) rather than whether any pixel is actually translucent. An image whose
source declares alpha - even if every pixel in it is fully opaque - now takes the lossless embed path
(larger, uncompressed) instead of JPEG. Images without a declared alpha channel are unaffected and
continue to JPEG-embed as before.
