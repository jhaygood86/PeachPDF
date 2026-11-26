# Wide-gamut and float canvases: what is kept exactly, and what is brought into range

A canvas with `colorType: "float16"` keeps floating-point pixels (`RasterSurface` made wide: premultiplied RGBA floats beside the eight-bit view). A solid color given as a `WideColor`
(a `color()` outside the sRGB gamut, converted into the canvas's space) and a picture of floats (an `ImageData` of `rgba-float16`, a bitmap or canvas made of one) are painted
and composited as floats (source-over, the Porter-Duff operators, plus; the blend functions work on colors brought into 0 to 1, as they are defined). Everything else is eight
bits: gradients, text, shadows, patterns and decoded images give eight-bit colors, which a float canvas then composites exactly but cannot have had in a wider range, and a
layer effect (blur, drop shadow, filter, SVG filter) works on the eight-bit view of its layer and gives that back (`RasterSurface.SyncWideFromBytes`), so a value outside 0 to 1
inside a filtered layer is brought into range. Display P3 and linear canvases of `unorm8` keep their pixels as bytes of their own space, with colors and pictures converted into it.

What stays open:

- Images carrying an ICC profile (a Display P3 PNG, a wide-gamut JPEG) are read as sRGB: PeachImage does not give the profile or convert to a space on decode. The
  `canvas-display-p3-drawImage*` and `-pattern-image` tests (about 400 subtests) wait for that.
- `toDataURL`, `toBlob` and `convertToBlob` write an sRGB PNG with no profile (a canvas in another space is converted to sRGB first), so a P3 or float canvas cannot be read back
  from its encoding at full range; the `*.convert` and `linear.float` encodings and `encode-hdr` (`toneMapping: "extended"`) wait for PeachImage to encode 16-bit and float PNG and
  JPEG XL with profiles.
- A shadow color is eight-bit sRGB (a `color()` shadow outside the gamut is brought into it), and a canvas of another space shown on a page is converted to sRGB as a picture
  (`PresentedInSrgb`) only when it has a surface.
- Win2D and Skia make wide surfaces (half-float targets); CoreGraphics does not (`CGBitmapContext` with float components needs a Mac to build and test), so its
  `CreateOffscreenSurface(w, h, wide: true)` answers null and a float canvas on it plays its recording on a floating-point raster canvas to be read. On Win2D layers, effects and
  Porter-Duff operators on a wide surface keep their values (the operator copy is of the target format, effects use half-float buffers); only a retained layer is eight-bit.
