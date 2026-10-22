# color-mix() in hsl and hwb now mixes the colours

`color-mix(in hsl, red, blue)` and `in hwb` painted black whenever the mixed hue crossed 0 degrees the short way (or took the
longer arc). They now give the colour a browser does (`rgb(255, 0, 255)` for red/blue). Gradients interpolated `in hsl`/`in hwb`
that crossed 0 degrees are affected the same way.
