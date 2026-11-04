# Skia decodes supported images lazily by default

**What changed.** `SkiaRenderContext`'s default `SkiaImageCodec` is now `Auto` (it was `PeachImage`). An image Skia has a codec for is kept as
its encoded bytes and decoded by Skia on first use (on a GPU surface, straight into the uploaded texture); anything else, and any image with an
orientation stored in its metadata, is still decoded by PeachImage. `PeachBrackets.Android`'s `HtmlView` gets this through the context it makes.

**What a document author would notice.** Pixels of a JPEG can differ by a few levels from before (and from the other canvases), and a file
with a colour profile is converted by Skia where PeachImage ignored the profile. An image damaged past its header now draws nothing instead of
failing to load. Pass `imageCodec: SkiaImageCodec.PeachImage` for the previous, cross-canvas-identical pixels.

**Verified against** `git show v0.9.26-preview1:docs/peachdrawing-skia.md`, where `PeachImage` is documented as the default.
