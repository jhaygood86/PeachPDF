# `color()` is read, and `none` is a missing color component

Before: `color(display-p3 ...)` and the other `color()` spaces were not read (a `background-color` using one was dropped, a keyframe holding one was ignored); `rgb(none 0 0)` was not read; `hsl(none ...)`/`hwb(none ...)` mixed as plain sRGB colours with the component as 0; `lab(... / none)` was opaque.
Now: `color()` is read in every predefined space and clipped to sRGB. `none` is 0 on its own (an alpha of `none` is transparent), and in a `color-mix()` or an animation it is a missing component that takes the other colour's value (an `rgb()`/`hsl()`/`hwb()` with `none` mixes in Oklab, like Chrome). `none` is not accepted in the legacy comma syntax.
