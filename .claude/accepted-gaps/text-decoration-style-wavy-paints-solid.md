# `text-decoration-style: wavy` paints as `solid`

Tracked by [#1114](https://github.com/jhaygood86/PeachPDF/issues/1114).

`wavy` cascades and computes correctly, and `TextDecorationStyleMapper` maps it to
`RDashStyle.Solid` because no dash pattern can express a wave — a decoration is stroked with
`RGraphics.DrawLine`, which has only a pen and two endpoints. Drawing one means building an
`RGraphicsPath` of half-period Béziers and stroking it with `DrawPath(RPen, RGraphicsPath)`, which is
a different shape of change from the `double` work that sits beside it: `double` is two more
`DrawLine` calls in the coordinate space the painter already uses, while a path is built in
`PixelsPerPoint`-divided coordinates (see `BuildRingPath`) and so has a unit question of its own to
settle for the pen width.

Deviation: [css-text-decor-3 §2.2](https://www.w3.org/TR/css-text-decor-3/#text-decoration-style-property)
says "`wavy` indicates a wavy line" — the one style it defines in its own words rather than by
cross-reference to `border-style`. Painting a straight one is a visible spec deviation, and an
unambiguous one.

Out of scope because the value is elsewhere: `wavy` is a spell-check/annotation idiom, whereas
`double` is the accounting convention for a grand total and was the case that prompted the work.

Chrome 141 reference, for whoever closes this: at a 16px font, rasterized at 300dpi, the wave occupies
about 2.5× the resolved thickness vertically and sits lower than the equivalent solid underline (rows
54–61 against 47–49).
