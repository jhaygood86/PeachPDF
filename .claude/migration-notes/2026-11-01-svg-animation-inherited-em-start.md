# An SVG animation starting from an inherited em length keeps the declaring element's size

Before: a CSS animation on an SVG element that started from a length inherited from an ancestor (`letter-spacing`, `word-spacing`, `stroke-width`, `stroke-dashoffset`) resolved a font-relative unit against the animated element's own font size, so `letter-spacing="2em"` on a 12px group animated from 40 on a 20px child instead of 24.

Now: the inherited length is a computed length, fixed where it was declared (24px here), so the animation starts from it.
