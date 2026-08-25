# `CssValueParser.ParseLength(Length, double, CssBox)` under-scales `em`/`rem`/`ex`/`ch` under a non-default `PixelsPerInch`

`ParseLength(Length length, double hundredPercent, CssBox box)` resolves an absolute length's catch-up
multiply (`* PixelsPerPointOf(box)`, issue #814's convention for landing in the box's internal,
`PixelsPerPoint`-inflated coordinate space) only on its `IsAbsolute` branch. Its `em`/`rem`/`ex`/`ch`
branch (via `Length.ToPixels(box.GetEmHeight(), box.GetRemHeight(), ...)`) applies no such correction,
on the assumption - stated in the method's own doc comment - that `GetEmHeight()`/`GetRemHeight()` are
"already self-consistently scaled...possibly `PixelsPerPoint`-inflated". That assumption is wrong:
`CssBox.GetEmHeight()`/`GetRemHeight()` live in the adapter's device-scaled *font-measurement* space
(`trueFontSizePt / PixelsPerPoint`, per `CssBox.NoEms`'s own doc comment and issue #631) - the opposite
direction. Confirmed empirically with a minimal box-tree harness: `<div style="font-size:20pt;
padding:2em;...">` resolves `ActualPaddingTop` to `40` at `PixelsPerPoint=1` (correct) but `20` at
`PixelsPerPoint=2` - half, not the DPI-invariant `80` every other absolute/box-geometry length in this
engine already produces under a non-default `PixelsPerInch` (issues #814/#821/#822).

This overload is the one used for the vast majority of `Length`-typed CSS box-geometry properties -
`padding-*`, `border-*-radius`, `outline-offset`, and (via the flex/grid/table layout engines and
`CssLayoutEngine` itself) `width`/`height`/`margin-*`/`min-*`/`max-*`/`top`/`right`/`bottom`/`left`/
column-gap/row-gap/etc. - so every one of those has presumably been silently wrong for `em`/`rem`/`ex`/
`ch` values under a non-default `PixelsPerInch` for as long as they've existed; invisible at the
library's default `PixelsPerInch` of 72 (`PixelsPerPoint == 1`, where the missing correction is a no-op).

Discovered and deliberately worked around locally while fixing issues #823/#824 (`CssImagePainter`'s
gradient stop-position/explicit-radius `em`/`rem` resolution): that fix does not route through this
shared `ParseLength(Length,...)` overload for `em`/`rem`/`ex`/`ch`, and instead applies the correct
`* pixelsPerPoint²` (device-scaling undone via `Length.ToPixels(box.GetEmHeight() * pixelsPerPoint, ...)`,
then the usual catch-up multiply) directly in `CssImagePainter.ResolveGradientLength`. Fixing
`ParseLength(Length,...)` itself - the same correction, moved into the shared method so every other
caller benefits - is a much wider-reaching change (every call site above needs its own regression
coverage, and some may have compensating logic elsewhere that a blind fix here could double-correct)
than #823/#824's literal scope. Tracked as
[#826](https://github.com/jhaygood86/PeachPDF/issues/826).
