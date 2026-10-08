# CSS animations rendered as a still frame (`PdfGenerateConfig.AnimationProgress`)

A PDF is one moment, so the feature is not playback: one number, `AnimationProgress` (null = off, 0 = start, 1 = end),
picks a point in **each animation's own run**, and the sampled values are written onto the box as ordinary declarations.
Layout and paint learned nothing about animation, which is why an animated `width` moves its neighbours for free.

## The load-bearing idea

`AnimationApplier.Apply` runs inside `DomParser.CascadeApplyStyles` **between the author-normal/inline-normal phase and the
author-`!important` phase** (step 5b). That is the animation origin's slot in CSS Cascade 4 §6.1 - above every normal
declaration, below every `!important` one - and it is why `#a { opacity: .6 !important }` correctly beats an animation
without any special casing. Do not move it to the end of the cascade: it would then beat `!important`.

- The `animation-*` longhands are stored on `CssBox` as **raw comma-list strings** (`css-properties.json`, area
  `GeneratedContentArea`, validated by each CSS-OM property's `ValueGrammar`, the same shape as `string-set`). They are
  read once, by the applier. Typed storage would have bought nothing: nothing else consumes them.
- `@keyframes` is collected into `HtmlContainerInt.Keyframes` **only when `AnimationProgress` is set**, so a document that
  does not use the feature pays nothing - not even the registry walk.
- Values are interpolated **as text** (`CssValueInterpolator`): both values are scanned into tokens and, when their shapes
  match, each differing number/colour pair is mixed and the result written back as text for the ordinary property setter.
  One rule covers numbers, lengths, angles, colours and any function/list built from them (`transform`, `filter`,
  `box-shadow`). Lengths of different units become a `calc()` and resolve at layout, so no layout-time knowledge is needed
  here. Anything of a different shape flips at 50% (CSS Values 4 §10.1 discrete).
- An implicit `from`/`to` keyframe takes the box's value at that moment (`CssUtils.GetPropertyValue`). Because animations
  are applied in list order straight onto the box, a later animation sees the earlier one's result - exactly the
  "underlying value" composition the spec describes.
- A snapshot is taken **inside** the run, so `animation-delay`, `animation-fill-mode` and `animation-play-state` do not
  decide whether it applies. Counting the delay would render a delayed animation untouched at `start`. The one exception is
  an animation with no run (zero duration or iteration count): only `forwards`/`both` leaves its final state behind.
- `infinite` has no end, so "end" is the end of its **first** iteration. With `alternate` that iteration still runs forward:
  `AnimationProgress = 1` shows the `100%` keyframe (the case that made the cross-fading logo page render as the *other*
  logo, which is what a reader expects).

## Found by running it, not by reading it

- **The `animation` and `transition` shorthands were broken in the CSS-OM** - any time value in them turned the rest into
  `initial` (`animation: fadeIn 10s` gave name `initial`). The unit tests passed 72 of 87 before this was found; every
  integration test using the shorthand failed. See
  [css-shorthand-converter-a-slot-must-consume-through-its-own-for-wrapped-converter](../invariants/css-shorthand-converter-a-slot-must-consume-through-its-own-for-wrapped-converter.md).
- **A non-positioned element with `opacity < 1` painted before every positioned box**, so the positioned header's
  background covered it: a half-transparent `<img>` inside a positioned container vanished entirely - hand-written
  `opacity: .5` as much as an animated one, and in both PDFium and MuPDF. The mid-cross-fade frame of the page that
  motivated this feature was blank until `FragmentPainter.PaintLayer` filed a stacking context created by
  opacity/transform/filter under Appendix E step 8 (CSS Color 3 §3.2). A `<div>` with opacity did not show it, because
  nothing positioned preceded it in those tests; `<img>` did, because the `position` of an image is carried by an
  anonymous wrapper box while the `opacity` stays on the `<img>` itself.
- An `inline-block` box with a `transform` is not painted at all - found while building the showcase, present before this
  change (toggling the paint-order fix off and on gave identical output), and filed as #1670. The animation showcase uses
  a block-level tile for that reason.
- `@supports (animation-name: ...)` is now **true**; five existing tests used `animation-name` as their example of a
  property that parses but is not rendered. They now use `transition-duration`, which still is.

## Deliberately not done

See [css-animation-snapshot-limitations](../accepted-gaps/css-animation-snapshot-limitations.md). Per-animation progress
(a dictionary keyed by `@keyframes` name) and an absolute time were left for later: the sampler already works per
animation, so both are additions, not rewrites.

## Evidence

`CssAnimationSnapshotTests` (timeline, easing, interpolation, cascade), `AnimationTransitionShorthandTests`,
`OpacityPaintsWithPositionedTests` (3 of its 4 fail with the paint-order fix reverted), CLI flag tests; the supplied
cross-fading page rasterized with both MuPDF and PDFium at none/start/0.45/0.5/0.55/end (the 45%-55% ramp of the
keyframes lands exactly there); the animation showcase (one PDF of three pages, built with `AddPdfPages` and a config
per page) rasterized with both.
