# An absolutely positioned inline `<svg>` (or `<math>`) ignored its offsets; background and border images and replaced content scale by the layout unit

Found by the WPT runner (`css/css-transforms/2d-rotate-001`, and the `article, svg { position: absolute }` pattern used across the CSS test
suites): an inline `<svg style="position:absolute;left:60px;top:220px">` stayed in the flow at the page origin and had a zero-width box,
at *any* `PixelsPerPoint` (probed at the default points layout too).

## Cause
`DomParser.CorrectAbsolutelyPositionedInlineElements` gives an absolutely positioned inline replaced box (`<img>`) a block wrapper that carries the
position and offsets, and makes the box itself static. It opened with `if (box is CssBoxSvg or CssBoxMath) return;`, meant to stop the walk from
*descending into* foreign content (issue #159) - but it ran before the check for the box itself, so an svg/math was never wrapped, while
`CorrectReplacedElementBoxes` had already reset its own display to inline. The early return now sits after the wrapper step, ahead of the
recursion into children only.

## Also fixed here: more pixel constants that ignored the layout unit
`BackgroundImageDrawHandler` (the CSS-pixel natural size of a background image), `BorderImageDrawHandler` (SVG intrinsic size and the number unit of a
slice) and `ReplacedContentRenderer` (an `<img>`/svg natural size under `object-fit`) multiplied by `Length.PointsPerPx` alone; they now also multiply
by the canvas's `PixelsPerPoint`, and the multicol one-pixel minimum column width by the same. css-backgrounds 274 -> 322.

## Found, not fixed
With no positioned ancestor, an absolute box is placed against the `html` root box, whose top is the body's collapsed margin (8px by default), not the
initial containing block (CSS 2.1 section 10.1): `top: 220px` lands at y=228 here and at 220 in a browser. It applies to every absolutely positioned
box, `<img>` included, and changing it moves existing documents, so it is its own change.

## Evidence
Full WPT run: +70, none lost; Engine.Tests 19,832, PeachPDF.Tests 1,978, PeachBrackets.Tests 1,214 green on net10.0.
