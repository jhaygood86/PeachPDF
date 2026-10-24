# Relative colour syntax is read

Before: `rgb(from red r g b)`, `hsl(from ...)`, `hwb(from ...)`, `lab(from ...)`, `lch(from ...)`, `oklab(from ...)`, `oklch(from ...)` and `color(from ...)` were not read: a property using one was dropped, and a keyframe holding one stood for the value the property has without the animation.
Now: the origin colour's components are channel keywords and the result is computed from them (`rgb(from #336699 calc(r + 20) g b)`), the alpha is the origin's unless written, and in an animation the colour is mixed in Oklab like `color()`.
