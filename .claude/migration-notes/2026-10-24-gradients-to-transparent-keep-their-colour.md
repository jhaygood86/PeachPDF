# A CSS gradient that fades to `transparent` keeps its colour

Before: `linear-gradient(to right, red, transparent)` interpolated the colour channels toward the transparent black `rgba(0,0,0,0)` without weighting by alpha, so the middle of the fade was a dull, grayish red.
Now: stops are interpolated premultiplied by alpha (CSS Color 4 §12.3), so the fade stays red while it becomes transparent, as in a browser. Any gradient with stops of different alpha is affected, in every
interpolation space, including hints and conic gradients. Opaque gradients and hard stops are unchanged. Also, a gradient `in lch` between a gray and a colour now takes the gray's hue from the colour, like `color-mix(in lch, ...)`.
