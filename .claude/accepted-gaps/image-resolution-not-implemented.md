# `image-resolution` is not implemented

Every raster is sized as 96 dpi (1px = 1/96in); the `image-resolution` property and any density stored in an image
file (JPEG JFIF/Exif, PNG `pHYs`) do not change an image's intrinsic size. Tracked in issue #1689.
