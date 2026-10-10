# A non-invertible `transform` no longer aborts rendering

**Before:** an element whose `transform` matrix has no inverse (`scale(0)`, `matrix(0,0,0,0,0,0)`, a rank-1
`matrix()`) made the whole render fail with `HtmlRenderException` ("Exception in box paint", inner `NotInvertible`), so no
PDF was written.

**Now:** the element and its subtree are not painted - it has no visible area, as css-transforms-1 specifies and
as browsers do - and the rest of the document renders. The same goes for a plane turned edge-on by a flat 3D
rotation (`rotateX(90deg)`, `rotateY(90deg)`, `rotateX(450deg)`): whether it was drawn used to depend on the box's
size, as a one-pixel hairline in PDF viewers; it is now left out once its projected thickness is under about
0.017 CSS px, where browsers stop drawing it. A plane that is nearly, but not quite, edge-on (`rotateX(89.9deg)`)
still shows as a faint line. Its layout box is unchanged, so surrounding content does
not move. An SVG `transform="scale(0)"` (or an all-zero `matrix()`) on a shape or group aborted the render the
same way and now simply draws nothing.
