# `image-rendering` is now honoured

**Before:** `image-rendering` was ignored. A raster image always used the renderer's default resampling (smooth when
shrunk, and whatever the PDF viewer chose when enlarged), and pixel art could not be kept crisp.

**Now:** `image-rendering: auto | smooth | high-quality | crisp-edges | pixelated` is an inherited property that applies to
`<img>` and other replaced images, `background-image` and `border-image`. `crisp-edges` and `pixelated` keep hard-edged
pixels (in a PDF: the image's `/Interpolate` flag is written false, which viewers may or may not honour); `smooth` and
`high-quality` allow smoothing. `auto` behaves as before, including the existing hard-edged default for repeating
background tiles and `border-image`.

**Why it matters to an author:** a page that already set `image-rendering` for a browser (typically `pixelated` on pixel art)
now gets that look in the PDF, where before it silently rendered smoothed. Confirmed against the last release tag: the
property was absent from `docs/html-css-support.md` and from `css-properties.json` at that tag.
