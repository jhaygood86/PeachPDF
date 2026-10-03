# SVG: `icc-color()` and gradient `color-interpolation`

Before: a paint such as `fill="#f00 icc-color(p, ...)"` was an invalid color, so the element silently took its inherited
fill; `color-interpolation` was ignored and gradients always blended in sRGB.

Now: `icc-color()` resolves through a matching `<color-profile>` (falling back to the sRGB color written before it), and
`color-interpolation="linearRGB"` on a gradient blends its stops in linear light, so such gradients render lighter in the
middle than before.
