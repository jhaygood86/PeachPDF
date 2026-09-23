# `ex` and `ch` are measured from the font; `em`/`rem` in SVG follow the declared `font-size`

**Before:** `ex` and `ch` were both a fixed `0.5em`, whatever the font. In SVG, `em`/`rem` in any geometry
attribute, `stroke-width` or `calc()` was a fixed 16px, ignoring the element's `font-size`. `cap`, `ic`, `lh`
and the root-element forms (`rex`, `rch`, `rcap`, `ric`, `rlh`) were not recognised at all — a declaration using
one was dropped (`git show v0.9.19:docs/html-css-support.md` lists only `em`/`rem`/`ex`, and `ch` as `0.5em`).

**Now:** `ex` is the font's x-height, `ch` the advance of its "0" glyph, `cap` its cap height, `ic` the advance of
"水", `lh` the used line-height, and each has a root-element form. A width written in `ch` or `ex` therefore shifts
for any font whose x-height/zero is not half an em (Source Code Pro's zero is 0.6em, so `10ch` grows by a fifth).
A font with no measurement to give (no OS/2 x-height, no glyph) keeps the old `0.5em`. In SVG, `em`/`rem` follow the
element's/root's declared `font-size` (an SVG that never declares one still gets 16px), and the measured units work in
SVG and MathML lengths too.

**Why:** these are the definitions in CSS Values and Units 4 §6.1; the `0.5em` was only ever the spec's fallback for
when measuring is impractical. Contexts with no font — media queries and `@page` lengths — still use the fallback.
