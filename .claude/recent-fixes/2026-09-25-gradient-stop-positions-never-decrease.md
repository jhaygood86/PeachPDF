# A gradient color stop before an earlier one takes the earlier one's position

Found comparing the v0.9.19 showcases against main: the `modern_colors` "over a checkerboard" swatches
sat on plain white. The `.checker` idiom, `repeating-conic-gradient(#ddd 0% 25%, #fff 0% 50%)`, painted
nothing at all - and two other showcases use the same rule.

## The bug

CSS Images 3 §3.4.3 step 2: a color stop whose position is less than an earlier stop's is raised to the
largest position before it. `#fff 0% 50%` after `#ddd 0% 25%` therefore becomes `#fff 25% 50%`, which is
what makes the two hard-stop pairs meet at a crisp edge. `NormalizeGradientStops` and `NormalizeConicStops`
(`Html/Core/Handlers/CssImagePainter.cs`) resolved unpositioned stops but never applied that step.

## What running it showed

Probes through the CLI, all with `#888 0% 25%, #fff 0% 50%`:

- **repeating conic**: blank.
- **repeating linear**: a soft blend where the hard edge belonged.
- **non-repeating linear, conic and radial**: a PDF MuPDF rejects with `subfunction 1 boundary out of
  range` - the stitching function's `/Bounds` were decreasing, which is invalid PDF, not just a wrong
  look. A strict reader could have refused the whole shading.

The equivalent `#ddd 0 25%, #fff 25% 50%` always worked, which is what isolated the cause to the missing step
rather than to the conic/repeat machinery.

## The fix

`ClampStopPositionsToRunningMaximum` runs on the explicit positions **before** unpositioned interior stops are
spaced, as the spec orders it (step 2 precedes step 3), so a spaced stop lands between neighbours that are
already ordered. An unpositioned last stop keeps its 100% (conic: 360deg) default but is raised the same way.
A single-stop list is left alone, since the old `first`/`last` handling for it is not the clamp's business.

## Evidence

Both renderers (PDFium and MuPDF) draw the checkerboards and the hard-stop linear/radial/conic cases
identically after the change, and MuPDF no longer reports the boundary error. New tests fail without the
call in each normalizer (linear, radial, conic Bounds/angles) and pass with it.

## Not done

Hint positions are not clamped separately: the existing `Math.Clamp((hintPos - s1) / range, ...)` already
bounds a hint between its two now-ordered neighbours.
