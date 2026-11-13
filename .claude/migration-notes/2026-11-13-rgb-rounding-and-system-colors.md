# rgb() rounds, and deprecated system colors are aliases

Before: a percentage component of `rgb()` was truncated (`rgb(0% 50% 0%)` was green 127, or in the modern syntax 50), and `Window`, `ThreeDFace` and the other deprecated system color keywords had values of their own (`Menu` was `rgb(247 247 247)`).
Now: components are rounded to the nearest byte (`50%` is 128), a decimal component is accepted, and the deprecated keywords compute exactly as the current one they alias (`Window` is `Canvas`, `Menu` is `Canvas`, `ThreeDShadow` is `ButtonBorder`), so a page that used `Menu` or `InfoBackground` sees `Canvas`'s value.
