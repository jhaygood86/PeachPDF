# Exif-rotated photos are drawn upright (`image-orientation: from-image`)

**Before:** an image (JPEG, WebP, PNG, TIFF, AVIF or JPEG XL) whose stored orientation was not upright (a phone photo taken in portrait, say) was drawn as
its stored raster, so it appeared sideways or mirrored in the PDF. Nothing in the engine read the tag
(`git show v0.9.21`: no orientation handling anywhere in `src/PeachPDF`, and `docs/html-css-support.md` has no entry for
the property).

**Now:** the CSS initial value, `image-orientation: from-image`, applies the tag, as browsers do: the image is drawn
upright, and its intrinsic width/height are the upright ones (a stored 4000x3000 image tagged 6 is 3000x4000), so
`<img>` without a size, `background-size: auto`, `object-fit` and `border-image-slice` all work on the upright picture.
Applies to `<img>`, `<object>`/`<video poster>`, `background-image`, `list-style-image`, `content: url()` and
`border-image`; SVG and gradients have no orientation.

**To restore the old output** for a document: `image-orientation: none` (inherited, so one rule on `body` is enough).
`<angle> || flip` replaces the Exif rotation with an explicit one.

The embedded bytes are unchanged (DCT/PNG/GIF pass-through still byte-for-byte); the rotation is a content-stream matrix.
