# SVG arcs between diametrically opposite points honor their sweep direction

## What was wrong

The PDFsharp endpoint-to-center arc conversion normalized the start/end angles by comparing the
large-arc flag with whether the raw angular difference was less than 180 degrees. For an exact
semicircle the difference is exactly 180 degrees, so that comparison cannot distinguish the two
possible sides. Its result then depended on endpoint ordering.

In a path made from two clockwise semicircles, the first arc was consequently reversed while the
second was not. Both halves occupied the left side and the intended circle rendered as one
semicircle drawn twice.

## The fix

The center calculation still uses the large-arc and sweep flags to select the correct ellipse.
After that selection, the angular difference is now normalized directly from the requested sweep
direction: clockwise sweeps are positive and counterclockwise sweeps are negative. This also makes
the 180-degree case unambiguous, where the large-arc flag has no geometric effect.

The regression test sends the reported SVG through `SvgTreeBuilder`, `SvgRenderer`, and the real
`GraphicsPathAdapter`, then inspects the generated Bezier geometry and requires the path to reach
all four extrema of the circle. A second test covers the counterclockwise normalization branch.

## Evidence

- The new exact-reproduction test failed before the fix with a maximum x-coordinate of 50 instead
  of 90, then passed after the fix.
- Targeted SVG path and PDF graphics tests: 41 passed.
- Changed production lines: 100% line and branch coverage.
- Full net8.0 suite: 10,420 passed, 9 skipped, with one unrelated existing
  `FontSynthesisIntegrationTests` locale-sensitive assertion failure.
- Whole-solution build: successful.
