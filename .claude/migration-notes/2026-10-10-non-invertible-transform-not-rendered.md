# A non-invertible `transform` no longer aborts rendering

**Before:** an element whose `transform` matrix has no inverse (`scale(0)`, `matrix(0,0,0,0,0,0)`, `rotateX(90deg)`
...) made the whole render fail with `HtmlRenderException` ("Exception in box paint", inner `NotInvertible`), so no
PDF was written.

**Now:** the element and its subtree are not painted - it has no visible area, as css-transforms-1 specifies and
as browsers do - and the rest of the document renders. Its layout box is unchanged, so surrounding content does
not move. An SVG `transform="scale(0)"` (or an all-zero `matrix()`) on a shape or group aborted the render the
same way and now simply draws nothing.
