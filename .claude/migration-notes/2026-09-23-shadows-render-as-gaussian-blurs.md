# `text-shadow` now paints; blurred `box-shadow` and `drop-shadow()` are real Gaussian blurs

**Before** (confirmed against the `v0.9.19` docs, which listed `text-shadow` under unsupported features and described the blur as "a vector approximation ... not a true Gaussian"):

- `text-shadow` was parsed and ignored — the text painted with no shadow at all.
- A blurred `box-shadow` was a stack of concentric alpha-stepped fills: a rough falloff, no knock-out (the shadow filled the whole shape behind the box, so it showed through a transparent element), and a rounded `inset` shadow's inner edge had square corners.
- `filter: drop-shadow()` shadowed the element's border-box **rectangle**, whatever the content was.

**Now:**

- `text-shadow` paints (comma-separated layers, first on top, default colour = the text's `color`, inherited). A shadow with no blur is the text drawn again at the offset; a blurred one is a Gaussian-blurred bitmap under the text (blur radius = 2 × standard deviation). The text itself stays vector and selectable. Not painted for vertical writing modes or for underline/line-through.
- A blurred `box-shadow` is a real Gaussian (blur radius = 2 × standard deviation): softer, longer-tailed falloff than before, and the outer shadow is **knocked out under the box's own border box** (rounded corners followed), so it no longer shows through a transparent element. A rounded `inset` shadow's inner edge now has rounded corners. Zero-blur shadows are unchanged.
- `filter: drop-shadow()` follows the element's **real alpha shape** (glyphs, a PNG's transparent corners, an SVG outline). The third length is the **standard deviation itself** (Filter Effects 1), not twice it as for `box-shadow`, so a given number gives a tighter blur than the old rectangle approximation did. A `drop-shadow()` now shadows the result of the `drop-shadow()`/filters before it, in list order.
- A document requesting **PDF/A-1** or **PDF/X-1a/X-3** that used a `drop-shadow()` with no blur and an opaque colour used to generate (an opaque hard shadow needed no transparency); every `drop-shadow()` is now a bitmap and is rejected there, like the other raster filters. Blurred shadows were already rejected (their concentric fills were semi-transparent).
- A `drop-shadow()` element is now a bitmap subtree: its own text is not selectable (a shadow on plain text via `text-shadow` keeps the text vector and selectable).
