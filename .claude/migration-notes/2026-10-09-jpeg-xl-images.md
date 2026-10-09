# JPEG XL images render

**Before** (through v0.9.21, PeachImage 0.4.x): an image whose content is JPEG XL (`<img>`, CSS images, `data:image/jxl`,
SVG `<image>`, `<picture><source type="image/jxl">`) failed to decode, so the image was silently omitted, and a
`<picture>` `<source type="image/jxl">` was skipped as an unsupported type in favour of its fallback.

**Now** (PeachImage 0.5.0): JPEG XL decodes in-process (decode only). A file made by recompressing a JPEG is turned
back into that JPEG and embedded under the ordinary JPEG rules; every other file decodes to RGBA. A `<source type="image/jxl">`
is chosen. Animations render their first frame.

**Why it matters:** a document that carried a `.jxl` photo had a hole where the photo should be.
