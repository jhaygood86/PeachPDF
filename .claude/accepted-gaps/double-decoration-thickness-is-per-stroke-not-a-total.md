# A `double` decoration's thickness is per stroke, not a three-band total

Tracked by [#1121](https://github.com/jhaygood86/PeachPDF/issues/1121).

[css-text-decor-3 §2.2](https://www.w3.org/TR/css-text-decor-3/#text-decoration-style-property) does not
define the decoration styles itself — it says their "values have the same meaning as for the
`border-style` properties", and
[css-backgrounds-3](https://www.w3.org/TR/css-backgrounds-3/#border-style) defines `double` as "Two lines
... **the sum of the two lines and the space between them equals the value of `border-width`**".

`FragmentPainter.StrokeDecorationSegment` does not follow that through: each stroke is the resolved
`text-decoration-thickness` and the gap matches, so the pair spans three times it rather than dividing it.

Deviation, deliberately: `border-width` is a total, while `text-decoration-thickness` is a stroke width —
`from-font` reads the font's own `underlineThickness` and `auto` is this engine's single-line thickness,
neither of which describes a three-band total. Dividing it would render the initial 1px double underline
as two ⅓px hairlines with a ⅓px gap, fainter than the `solid` underline it is a heavier version of.

Chrome 141 agrees, measured at 300dpi with a 16px font: its first stroke sits exactly where the solid
underline does (rows 47–49 for both) and the second at rows 56–58, so it also treats the thickness as
per-stroke and lets the total grow.

The thing to keep straight when revisiting: this is a deviation from what §2.2 *points at*, not a choice
in a space the spec leaves open. §2.2's own text is a bare cross-reference with no carve-out.
