# Win2D decodes supported images on the device by default

**What changed.** `Win2dRenderContext`'s default `Win2dImageCodec` is now `Auto` (it was `PeachImage`). An image Windows has a codec for is
kept as its encoded bytes and decoded by Windows straight into the device's bitmap; anything else, and any image with an orientation stored in
its metadata, is still decoded by PeachImage. `PeachBrackets.WinUI`'s `HtmlView` gets this through the context it makes.

**What a document author would notice.** Pixels of a JPEG can differ by a few levels from before (and from the other canvases), because the
two decoders round their inverse transforms differently; colour-managed and CMYK files may convert differently. PNG, GIF and BMP match to
within rounding. Pass `imageCodec: Win2dImageCodec.PeachImage` for the previous, cross-canvas-identical pixels.

**Verified against** `git show v0.9.26-preview1:docs/peachdrawing-win2d.md`, where `PeachImage` is documented as the default.
