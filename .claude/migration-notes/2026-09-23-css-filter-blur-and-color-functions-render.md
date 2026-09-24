# `filter: blur()`, `grayscale()`, `sepia()`, `saturate()` and `hue-rotate()` now render

**Before:** these five functions parsed but contributed nothing — `filter: blur(4px)` painted the element sharp, and `filter: grayscale(1)` left it in colour (confirmed against the `v0.9.19` docs, which described them as accepted but having "no visual effect of their own"). Only `opacity()`, `brightness()`, `contrast()` and `invert()` did anything.

**Now:** an element whose `filter` list contains one of them with a real effect is rendered into a bitmap, filtered in the order written, and embedded, so it looks the way it does in a browser. The bitmap is rendered at `PdfGenerateConfig.RasterizationDpi` (default 300; new, along with `MaxRasterPixels` and the CLI's `--raster-dpi`) and placed at exactly the size of the element plus room for the blur, so the page layout is unchanged. What a document author can notice:

- The element and everything inside it is a **bitmap**, not vector. It is sharp at any zoom, but its text is **no longer selectable or searchable**, and the file grows by the compressed size of the bitmap.
- The whole `filter` list runs in the raster pass once one of the five appears: `filter: brightness(1.5) blur(2px)` is one bitmap, where `brightness(1.5)` alone stays native PDF colour math.
- A document that requests **PDF/A-1** or **PDF/X-1a/X-3** conformance and uses one of these functions used to generate (the functions did nothing); it now throws `InvalidOperationException`, because a soft-edged bitmap needs a transparency soft mask those levels forbid.

`blur(0)`, `grayscale(0)`, `saturate(1)` and `hue-rotate(0deg)` change nothing and stay vector, so documents using them as a no-op default are unaffected.
