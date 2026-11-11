# On the Win2D canvas, a blur or shadow with unequal deviations is no longer blurred by their mean

- A `BlurEffect` or `DropShadowEffect` whose horizontal and vertical standard deviations differ (for example SVG `stdDeviation="8 0.5"`, or a
  CSS shadow drawn through the Win2D `HtmlView`) used to be blurred in both directions by the mean of the two, so a wide, flat blur came out round.
  It is now blurred sideways by the first deviation and up and down by the second, which is what the effect asks for. Equal deviations, and
  every other canvas, are unchanged. `PeachDrawing.CoreGraphics` still uses the mean (its blur takes one radius).
- Confirm with `git show v0.9.26:docs/peachdrawing-win2d.md`: that release listed the mean as a limitation of the Win2D canvas.
