# Custom properties in `@keyframes`

`RegisteredKeyframes.IsAnimatable` no longer drops `--*`. `AnimationApplier.ApplyCustomProperty` samples the property like any
other but writes the result to `box.CustomProperties`, where the cascade's last step (the deferred `var()` pass, after the
animation step) resolves it, so `width: var(--w)` and every descendant that inherits `--w` see the animated value.
Unregistered: `MixCustomProperty` flips (`t < 0.5` is the from value, so exactly 0.5 is the to value, as in Chrome).
Registered: `RegisteredProperty.InterpolableTypeOf` picks the one interpolable `syntax` component both values match and the
ordinary `CssValueInterpolator` mixes them (`<integer>` rounds); anything else is discrete.

Traps: an `!important` custom property is stored on the box before the animation (`PreApplyImportantForAnimation`) and
outranks the animation origin, so that method now also reports the names, and the sampler skips them. The underlying value for
a keyframe that leaves the property out is the box's cascaded custom property, else the registered initial value. A global
keyword in a keyframe resolves in `ResolveCustomGlobalKeyword` (the generic one reads `CssUtils.GetPropertyValue`, which knows
no custom property). Evidence: `AnimationCustomPropertyTests` (the issue's Chrome table at 0.1/0.5/0.9).
