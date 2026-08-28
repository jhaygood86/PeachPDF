# `box-shadow` blur approximation band count ignores non-default `PixelsPerInch`

`FragmentPainter.Decorations.BlurSteps(double blur)` decides how many concentric ring/rect fills
approximate a blurred `box-shadow`'s falloff: `Math.Clamp((int)Math.Round(blur * 2), 6, 40)`. The `blur`
value it reads is already `PixelsPerInch`-inflated layout-space (per issue #814's absolute-length scaling
contract, applied before `BlurSteps` is called), and `BlurSteps` has no `PixelsPerPoint` awareness of its
own - so the number of blur layers scales with `PixelsPerInch`, even though each individual ring's
position/size is correct (fixed for [#812](https://github.com/jhaygood86/PeachPDF/issues/812)'s
`BuildRingPath`/`BuildLayerRoundRect`).

Confirmed: a `box-shadow: inset 0 0 10pt black` renders with 20 concentric rings at the default
`PixelsPerInch = 72`, and 40 at `PixelsPerInch = 144` - double the layers, for the identical declared blur
radius.

Not a "wrong position" bug - purely a rendering-*quality* difference (a smoother or coarser blur
approximation depending on `PixelsPerInch`, clamped to a 6-40 band range so it's never dramatically
coarse). Distinct from, and lower-severity than, the position/leak bugs #812 fixed; deliberately not
bundled into that fix to keep it scoped to what was actually reported.

Discovered while adding #812's own regression test coverage for `BuildRingPath`
(`BoxShadowPaintIntegrationTests.BlurredInsetShadow_RingBounds_StayWithinPaddingBoxUnderNonDefaultPixelsPerInch`)
- an earlier draft of that test compared ring counts across two `PixelsPerInch` values and spuriously
failed on this unrelated difference before being redesigned to assert bounds directly instead.

Tracked as [issue #852](https://github.com/jhaygood86/PeachPDF/issues/852); fix sketch there is dividing
`blur` by the ambient `PixelsPerPoint` before calling `BlurSteps` (or passing it in and dividing there).
