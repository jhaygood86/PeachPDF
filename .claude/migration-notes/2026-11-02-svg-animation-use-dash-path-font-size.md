# CSS animation on inline SVG: `<use>`, `font-size`, dash lists, `d` and a negative keyframe

In an inline `<svg>` with an animation sampled at a given progress:

- A keyframe that leaves a property out (`@keyframes k { to { fill: red } }`) on an element reached through a `<use>` now starts from the value the `<use>` gives it (`<use fill="#00f">` mixes blue to red), where it started from the referenced element's own ancestors (black, with none). The same holds for `stroke-width`, `font-size` and every other inherited property.
- An ancestor's `font-size` in `em` or `%` (`g { font-size: 2em }`) is the length it computes to for an animation below it, where it was resolved against the animated element's own size (the text of the example animated from 40px, and now from 20px, to a `font-size: 40px` keyframe).
- `stroke-dasharray` lists of different lengths mix (`2 4` against `10 20 30`, repeated to six items) where they flipped at the midpoint. `none`, a pair of units (`4px` against `10%`) and a `calc()` item still flip.
- `d` in a keyframe (`path("M0 0 L10 10")` to `path("M10 10 L30 30")`, or against the attribute's own data) mixes number by number when the commands are the same type, where it flipped at the midpoint.
- A negative number in a keyframe of `r`, `rx`, `ry`, `width`, `height`, `stroke-width`, `font-size` or `stroke-dasharray` is dropped as invalid, as a browser drops it, where it was mixed (a negative radius drew a small circle). A `calc()` of constants that comes out negative is dropped as well, where a browser clamps it to 0.
