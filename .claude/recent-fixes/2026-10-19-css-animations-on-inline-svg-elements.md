# CSS animations on the elements of an inline `<svg>`

Second step of the #1672 item "CSS animations on the elements of an inline `<svg>` are not applied" (the first made the properties reach a stylesheet and a keyframe at all, see
[the SVG stylesheet properties note](2026-10-16-svg-stylesheet-stop-colour-bare-numbers-and-css-transforms.md)).

## The idea

An HTML box is animated by writing the sampled values onto the box as declarations, because layout and paint then read them like any other. The elements of an SVG are not boxes anything reads: `SvgTreeBuilder`
resolves each property as it builds the scene graph, through `ResolveStyledAttr` (inline `style=""` > matching rule > presentation attribute). So the animation origin is a lookup there, not a write:
`ResolveStyledAttr` asks the source node for `GetAnimatedValue(name, underlying, parent, initial, inherited)` and uses it above those tiers unless an `!important` declaration (a rule's, or the inline style's)
sets the property. `AnimationApplier` is now `partial`: `PlanElement` works out an element's animations once (kept on the box, `BoxRareState`), `SampleElement` folds them for one property, reusing `Sample`,
`ResolveVariables` and `CssValueInterpolator` as the HTML path does (`Sample` now takes the underlying value, `ResolveStops` a keyword resolver, so the HTML path is the same code with different callbacks).

## Found by running it against Chrome (81 cases, 256 samples; a paused animation with a negative delay, `getComputedStyle`)

- The first run was 35 of 256 off. Causes: `stroke-width: 2` against `10px` (a bare number and a length of another unit mix as a `calc()`, not as 2px to 10px, so a bare number is put in px before mixing:
  `UserUnitProperties`); a `transform` presentation attribute is in the attribute grammar (`translate(10 20)`), which the interpolator cannot read, so as the underlying value it goes in as the matrix it means
  (`UnderlyingValue`); `revert` in a keyframe is the UA-level value (unset), not the value without the animation (that is `revert-layer`, the animation origin being a layer of its own); my harness sampled `p` seconds
  into the animation where `AnimationProgress` is a fraction of the whole run (`total` in the cases).
- The underlying value of a property an element does not declare is what it inherits, so it is found by asking the parent (`ResolveEffectiveValue`), recursively, and an animated parent's value is what the child
  inherits. The inherited-ness of an SVG property is a list in the builder (`InheritedSvgProperties`, from css-properties.json): the generated registry exposes no such flag.
- Nothing is allocated for an element with no animation: the closures live in `ApplyAnimation`, called only when `IsAnimated` (a closure over a method's parameters is allocated at method entry, so checking `IsAnimated` first in `ResolveStyledAttr` itself did not avoid it: +11% allocation on a 5000-rect document, measured).
- The parent's value is looked up once per property per element (`ParentValue` memo): both the underlying value and an `inherit` keyword ask for it, and each ask walks the whole ancestor chain, which doubled the work per nesting level (20 nested animated `<g>` with an `inherit` keyframe: 14 GB allocated; 40 never finished).

## Review 1 (a second Chrome differential of 183 cases) found, and this fixed

- An `inherit`/`initial`/`unset` declared on the element (attribute or `style=""`) went into the interpolator as the word, so the animation flipped; `UnderlyingValue` now resolves CSS-wide keywords first.
- The bare-number-as-user-units rule covered only the stroke lengths; `<circle r="10">` against `r: 30px`, `x`, `width`, `font-size`, `letter-spacing` flipped. `UserUnitProperties` is widened to the geometry and text lengths.
- An animation whose steps all dropped (an unresolvable `var()`) returned its px-normalised copy of the underlying value, which then won over the declaration; it now returns null so the declaration stands.
- Geometry already goes through `ResolveStyledAttr` (since the geometry-as-CSS change), so it animates; the first docs/notes draft said it did not.

## Review 2 and 3 found, and this fixed

- An `inherit` keyframe on nested animated elements was exponential (each level asked the parent twice); the parent's value is now memoised per element and property (`ApplyAnimation`).
- A keyword declared on an ancestor (`<g fill="inherit">` above the animated rect) reached the child raw: `ResolveEffectiveValue` now normalises it as the element's own declaration is.
- A property nothing declares and the registry has no initial for (`x`, `y`, `cx`, `cy`, `r`, `letter-spacing`, `word-spacing`, `font-size`) had no underlying value, so the animation held its keyframe value for the whole run; they now have one (`InitialValuesOutsideTheRegistry` (since folded into the registry, see [the invariant](../invariants/svg-a-property-the-builder-reads-needs-an-svg-registry-entry.md)); `font-size` is the builder's resting size).
- The test for an all-steps-dropped animation used `fill`, for which the px-normalised copy equals the raw value, so it pinned nothing (found by mutation); it now uses an ancestor's `200%` font size.

## Not done

- `points` and gradient `offset` (read as attributes), `visibility` (the SVG builder never reads it, so listing it was wrong; #1797), `filter`, and the individual transform properties.
- `d` is a discrete flip (Chrome mixes the two paths): #1798.
- `<use>` underlying values, ancestor em/% values and unequal `stroke-dasharray` lists: #1798. `@keyframes` across `@layer`, quoted names: #1799.
- A standalone SVG (`<img src="x.svg">`): no container, no progress, its `@keyframes` are its own `<style>`.
- A keyframe declaring a shorthand (`font`, `marker`) is not expanded for an SVG element; a longhand is.
