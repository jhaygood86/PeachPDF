# A repeating background image now fills the area under a transparent border

Before: with the default `background-clip` (border-box) and `background-origin` (padding-box), a `repeat`/`repeat-x`/`repeat-y`/`space` image stopped at
the padding box, so a transparent or translucent border showed the page and not the image. Now the tiles continue outward under the border, as in a
browser. Also new: `background-clip: border-area`; and an SVG background with no `viewBox` or size is drawn at the size the background gives it.
