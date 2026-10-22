# color-mix() keeps a gray's hue when it is written in the mix's own space

`color-mix(in oklch, oklch(0.5 0 120), oklch(0.6 0.2 240))` used to take the second colour's hue (240) for the gray; it now mixes the hues as written
(180), as a browser does. The same for `lch`, and for a `lab()`/`oklab()` operand in an `lch`/`oklch` mix. A gray written any other way (hex, `rgb()`) still takes the other colour's hue.
