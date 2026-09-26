# Face matching narrows by width, then style, then weight; `font-weight` takes fractions

**Before** (checked against `v0.9.20`, where the resolver narrowed slant first and `font-weight` was validated as an integer):

- A family's faces were narrowed by slant first, then width, then weight. For a family whose faces differ in both width and style, such as
  a condensed upright face and an italic face of normal width, `font-stretch: condensed; font-style: italic` was set in the italic face of
  normal width. Among several faces that declare an oblique range (`font-style: oblique 0deg 10deg`, `oblique 20deg 30deg`) the one
  declared last won whatever angle the box asked for.
- `font-weight: 350.5` was not a valid value: the declaration was dropped, and the box kept the weight it inherited. A weight was a whole
  number all the way to the font.
- An installed variable font (the ones the operating system ships) took part in matching at its default weight and width only.

**Now:**

- Faces are narrowed as CSS Fonts 4 section 5.2 orders it: by `font-stretch`, then `font-style`, then `font-weight`. The condensed italic
  box above is set in the condensed upright face, and its lean is faked. A document that styled such a family and relied on the italic
  face of normal width being chosen for condensed italic text now gets the condensed face.
- Among oblique ranges, `font-style: oblique <angle>` takes the range that holds the angle or else the nearest one (ranges above the angle
  first from 11 degrees up, ranges below it first for smaller angles), whichever order the `@font-face` rules were declared in. A face
  declared `italic` is preferred to an oblique range for `font-style: italic`, and an oblique range to an `italic` face for
  `oblique <angle>`.
- `font-weight` takes any number from 1 to 1000, fractions included, in the property, the `font` shorthand and `calc()`. A variable font's
  `wght` axis is set to the fraction, and a face whose range holds `350.5` is preferred to one that only holds 350.
- An installed variable font covers the ranges of its own axes, like an added one with no descriptors, so a family that also has static
  faces now lets the variable one compete for every weight, width and slant it can draw.

Documents whose families do not differ in both width and style, that use whole weights, and that use no installed variable font, render as
before.
