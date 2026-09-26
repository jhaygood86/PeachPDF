# `@font-face` ranges and `font-stretch` percentages

**Before:** the `@font-face` descriptors took one value each. `font-weight: 100 900`, `font-stretch: 75% 125%` and
`font-style: oblique 0deg 14deg` were not carried as ranges (the face was matched at its file's own weight), and the `font-stretch`
property rejected a percentage such as `87.5%` (only the nine keywords worked). A variable font was set to whatever weight, width and
slant each box asked for, whatever its rule declared.

**Now:**

- The three descriptors take ranges (and `auto`): a face covers every value inside its range in font matching, and the weight, width and
  slant of the text set a variable font's `wght`, `wdth` and `slnt` axes **kept inside the range**. A rule that declares
  `font-weight: 300 600` draws `font-weight: 900` text at 600 (and fakes the bold that the range cannot reach); a rule that declares a
  single value, such as `font-weight: 700`, holds a variable font at that value. A document whose `@font-face` rule for a variable file
  declared a narrower weight than the text asks for now renders at the declared end, as in browsers.
- A variable font with no descriptors covers the ranges of its own axes, so in a family that also has static faces the variable file
  now competes for every weight, width and slant it can draw (it used to be matched as a single face at its default weight).
- `font-stretch` takes a non-negative percentage of the normal width (`87.5%`); the keywords stand for percentages (`condensed` is 75%),
  and a variable font's `wdth` axis is set to it. The `font` shorthand still takes only the keywords.
- `font-style: oblique <angle>` sets the `slnt` axis to the angle (kept inside a declared range) instead of shearing the glyphs; a face
  declared `oblique 0deg 14deg` also serves upright text.

Documents that use static fonts, or a variable font with no range in its `@font-face` rule and no `font-stretch` percentage, render as
before.
