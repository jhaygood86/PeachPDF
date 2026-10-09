# `image-orientation` (from-image / none / angle + flip)

**Idea.** The orientation is a draw-time matrix, never a re-encode. `Image.Width/Height` stay the stored raster's
dimensions (PdfImageTable's downscale decisions, `GraphicsAdapter.IsWholeImage`/crop placement and every
pass-through embed depend on raw dims and an unrotated local rect). `ImageOrientationPainter.Draw` pushes a matrix that
maps the unrotated local rect (the destination with width/height swapped for a quarter turn, same centre) onto the
destination, draws, pops. The identity orientation makes exactly the old call with no `PushTransform`, so all existing
PDF output is unchanged. Sizes are read through `ImageOrientationResolver.OrientedSize` at every site that reads a
raster's intrinsic size (CssBoxImage incl. the srcset divisor, `MeasureImageSize`, ReplacedContentRenderer natural size,
background intrinsic size/ratio, border-image natural size).

**Traps.**
- No PeachImage decoder applies orientation as of 0.5.1 (pixels and `ImageInfo.Width/Height` are always as stored); it
  reports it as `ImageInfo.Orientation` for JPEG/TIFF Exif, PNG `eXIf`, WebP `EXIF`, AVIF `irot`/`imir` and the JPEG XL
  header. `PeachImageSource.Decode` copies that (EXIF values 1-8) onto the source. 0.5.0 differed: its JPEG XL decoder
  rotated the pixels itself, which made `none` unable to undo it and needed a hand-written Exif byte reader; both are
  gone with 0.5.1. A recompressed JXL re-enters `Decode` as its rebuilt JPEG and gets that JPEG's tag; any other JXL
  takes the orientation from its own header (`DecodeJxl`). PNG `eXIf` after the image data is not seen by `Identify`.
- Exif -> (clockwise quarter turns, flip applied after rotating) is defined once in `ImageOrientation.FromExif`;
  `ImageOrientationTests` checks all 8 against the Exif definition on a pixel grid and the painter against all 8
  corner placements.
- border-image slices are in the *oriented* picture. `ImageOrientationPainter.ToStoredSpace` maps each slice rect back
  through the inverse matrix, then each region is drawn with its own matrix; the 9-slice maths needed no change.
- Gradient/tile images in `DrawBackgroundImage` (`intrinsicSizeInCssPixels: false`) are never oriented (spec: no
  orientation for generated images).
- The grammar is one class (`ImageOrientation.TryParse`) used by the CSS-OM converter and the render layer; the property
  is stored as declaration text (like `object-position`) so the generator needed no new data type. `calc()` angles are
  rejected (issue #1688). Units are matched case-insensitively.
- Pre-existing, unrelated: the `border-image` *shorthand* with a `/` (`border-image: url(..) 8 / 12pt`) throws
  `IndexOutOfRangeException` in `PeriodicValueConverter.PeriodicValue.ExtractFor` during stylesheet composition.

**Evidence.** `ImageOrientationTests` (92 cases). A grid of 16 images (Pillow-written JPEG and PNG for each of Exif 1-8,
the stored raster being the inverse transform of an asymmetric F, cross-checked with `ImageOps.exif_transpose`)
rasterized with PDFium and MuPDF: all upright; `none` shows the stored raster; background, object-fit and border-image
variants upright too.
