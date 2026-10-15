# `currentcolor` as a gradient stop colour now draws

A gradient with `currentcolor` as a colour stop (`background: linear-gradient(to right, currentcolor, red)`, also `radial-gradient()`,
`conic-gradient()`, their repeating forms, in `background`, `background-image` and `border-image-source`) used to be
dropped whole by the CSS-OM grammar, so the box drew no gradient at all. The stop now takes the box's own `color` when the
gradient is painted, as for any other `<color>`.

Before this change (`git show v0.9.22:src/PeachPDF/CSS/ValueConverters/GradientConverter.cs`) a stop colour was validated with the plain colour
converter, which does not accept the keyword.
