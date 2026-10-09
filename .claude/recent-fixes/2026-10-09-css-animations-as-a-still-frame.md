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

## Review follow-ups (traps worth knowing)

- **A `var()` author declaration is still *pending* when the animation step runs** (the cascade resolves them at its very
  end). Two bugs fell out of that: the implicit 0%/100% keyframe read the UA default instead of the author's value
  (`width: var(--w)` + `to { width: 400px }` flipped from `auto` at 50%), and a pending entry for an animated property resolved
  *over* the animated value afterwards. `Apply` now resolves the pending entries that overlap the animated properties
  (`DomParser.ResolveDeferredVarProperties(..., only)`) before sampling, and removes them. "Overlap" includes a pending
  *shorthand* whose longhand is animated - `margin: var(--m)` with `margin-left` keyframes used to overwrite the animation
  with `--m`; found by a test, not by the review.
- **A shorthand with `var()` stays whole in a keyframe** (the CSS-OM only splits a shorthand it can see through), so a
  resolved keyframe value is expanded into longhands (`ExpandShorthand`) before sampling; the registry has no setter for the
  shorthand name itself.
- **Global keywords in keyframes** `inherit`/`initial`/`unset` are kept and resolved against the box at sample time (same arms
  as `AssignCssBlock`); `revert` resolves to the cascade's UA-level snapshot (the animation origin counts as author origin for it, Cascade 5 §7.3.4 - the snapshot is only taken when a keyframe uses `revert`, via `HtmlContainerInt.KeyframesUseRevert`) and `revert-layer` to the value the box holds when the animation step runs (the animation origin is a layer of its own, §7.3.5).
- **Zero-duration `forwards`** shares `AnimationTimeline.EndOfActiveInterval` with the finished-run case, so a fractional
  iteration count ends at its remainder rather than at 1.
- `ClonePdfAConfig` in the TestHarness copies the config by hand (deliberately not attachments or FacturX, which PDF/A-2B rejects): a new property that is not copied silently
  makes the PDF/A sweep validate a different document from the showcase.
- **`@supports (animation-name: ...)` is true whether or not `AnimationProgress` is set** - decided, not overlooked. A
  browser answers `@supports` about the implementation, never about the user's settings (reduced motion leaves it true;
  `prefers-reduced-motion` is the author's hook, and PeachPDF already reports `reduce`). Gating it on the config would need
  the flag threaded into the stylesheet parser, since `DeclarationCondition.Check` runs at parse time with no context, and
  would make PeachPDF answer differently from a browser. Revisit only if a maintainer wants the gate.
- `animation-*` values from `var()` are read: the applier settles the deferred `animation*` declarations before reading
  them (same `ResolveDeferredVarProperties(..., only)` filter). Known edge: custom properties declared `!important` are not
  yet visible at that point - see the limitations note.
- **`!important` on `animation-*` is applied before the animation step** (`DomParser.ApplyImportantAnimationProperties`),
  because those properties decide which animations exist while the animation origin itself sits *below* the important
  phase. Without it `* { animation: none !important }` (the usual print-stylesheet reset) was ignored and
  `animation: fade 1s !important` never applied. Phase 6 re-applies the same declarations, to the same result.
- **A percentage's identity is 100%, not 1%**: `filter: none` mixed with `brightness(150%)` used to start from
  `brightness(1%)` (nearly black) because the neutral argument kept the token's unit. `CssValueInterpolator.TryIdentity`
  now scales it by 100 for `%`.

## Second review round (traps worth knowing)

- **The eased value may leave 0 to 1, and the sampler used to cut it there.** `eased <= 0 / >= 1` returned the literal end
  value, which flattened every overshooting `cubic-bezier()`; only exactly 0 and 1 keep the specified text now, and the
  interpolator extrapolates (colours and `opacity` still clamp).
- **Exactly at the first keyframe is the start of its interval, not "before it".** Returning the 0% literal for
  `progress <= firstOffset` skipped the interval's easing, so `step-start`/`steps(n, jump-start)` showed 0% at progress 0
  where a browser shows the first step. Only strictly before the first keyframe holds the first value.
- **`hsl()`/`hsla()` were scanned as a function of plain numbers**, mixing hue like a length (red to lime went through
  yellow). They are colour tokens now (the legacy colour syntaxes all interpolate in sRGB).
- **`display` between `none` and anything is a discrete step that shows the other value for the whole interval** (the same
  special case as `visibility`), not a flip at 50%.
- **The CSS-OM easing grammar rejected `jump-*`, `linear()` and, in the shorthand, string names - and `animation: inherit`
  was read as an animation called "inherit".** The longhand did not "work": it was dropped and the property kept `ease`,
  silently. Fixing the one grammar (`StepsConverter`, `LinearEasingConverter`, `AnimationNameConverter`, which refuses the
  CSS-wide keywords) fixed `animation`, `animation-timing-function` and `transition` together. `linear()` is validated for
  shape only and still read as plain `linear`.
- **`iteration-count: 0` with `forwards` rests at the start, not the end**: overall progress is the iteration count, 0.
- **`CreateDocument`/`AddPages` validate `AnimationProgress` too** (`PdfGenerator.ValidateAnimationProgress`), so the
  documented "anything else makes generation throw" holds for every entry point; they still do not animate HTML.
- **A behaviour test through the public API matters**: mutating `PdfGenerator` to never assign the progress to the container
  used to pass the whole suite, because the API tests asserted only `NotNull` and page counts. `GeneratePdf_PaintsTheSampledFrame`
  and `AddPdfPages_PaintsItsOwnConfigsFrame` read the rectangle widths out of the page content stream.
- The showcase is registered through `SaveStatesShowcaseAsync`, not `SaveShowcaseAsync`: one document of several pages, each
  the same HTML at another `AnimationProgress`, which `SaveShowcaseAsync` (one config, one render) cannot express. It writes
  the manifest entry itself, as `SaveDeclarativeShowcaseAsync` does, so the docs site card still appears.
