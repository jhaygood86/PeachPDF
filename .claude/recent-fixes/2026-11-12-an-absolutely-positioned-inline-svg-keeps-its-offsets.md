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

## Fixed afterwards: the initial containing block, and the root element's margins
With no positioned ancestor an absolute box was placed against the `html` root box, whose top is the margin the body and its first child collapsed
through it (`top: 220px` landed at 228 with an 8px body margin; a browser puts it at 220). Two parts, both from the spec:
- `CssBox` placement of an absolute box (left/top) now uses the initial containing block - the page area's corner, `HtmlContainer.MarginLeft/MarginTop`,
  as the fixed branch already did - when the nearest "positioned ancestor" is just the root (CSS 2.1 section 10.1).
- `DomUtils.EstablishesIndependentFormattingContext` now answers true for the root `<html>` element (sections 9.4.1 and 8.3.1: the root establishes a
  block formatting context and its margins do not collapse), so margins collapse up to the `html` box but not through it. Without this the first part
  alone made `html { height: 50% }` tests fail, because `html` itself still started 18px down.
  Consequences: the `html` box contains its floats; and a first child's oversized top margin is now an ordinary margin inside a container, so css-break-3
  section 5.2 truncates it at the unforced page break and the content starts the next page. Three tests pinned the old "margin collapses into the
  document root and paginates as blank space" behavior (`FragmentEmitterTests` x2, `PageBreakIntegrationTests` "known boundary"); they now pin the
  truncation (`margin-top: 900pt` on a 200pt page: slot 1, origin 160, where it was slot 5, origin 800). Ordinary documents lay out where they did:
  PeachPDF.Tests (1,978) and the Acid2 regression tests are unchanged.
  `RootElementMarginTests` pins the three placements. WPT: +4, none lost (css2-positioning 386, css-transforms 471).

## Evidence
Full WPT run: +70, none lost; Engine.Tests 19,832, PeachPDF.Tests 1,978, PeachBrackets.Tests 1,214 green on net10.0.
