# A keyframe `0px` on line-height stays a length (#2004)

**Symptom:** `@keyframes { from { line-height: 0px } to { line-height: calc(1em + 10px) } }` flipped at the middle where
browsers blend (7.5px / 22.5px at 0.25 / 0.75).

**Cause:** `Length.ToString` deliberately drops the unit of a zero length, so `Property.Value` for `0px` is `0`. The
interpolator treats a bare `0` as the *number* on `line-height`/`tab-size`/`border-image-width`/`border-image-outset`
(`ZeroIsANumberProperties`), which does not mix with a calc() of lengths. The unit was gone before the interpolator ran.

**Fix:** `RegisteredKeyframes.KeepZeroUnit` reads the authored text (`Property.Original`) for those four properties
when the serialized value is `0` and the author wrote a unit (`0px`, `0em`), so the interpolator sees a length. A bare
`0` is untouched and still flips (existing tests keep that).

**Tried and rejected:** making `Length.ToString` always keep the unit. It is the right CSSOM serialization in principle,
but the zero-omits-unit convention is pinned by dozens of CSS-OM tests (margin, padding, gradients, clip, text-shadow,
the longhand baseline snapshot), so it is a separate, much larger change.

**Not done:** transitions are not covered (they interpolate computed values, not keyframe text).

**Evidence:** four new cases in `AnimationCalcLengthTests` (`0px`, `0em`, both directions); engine and PeachPDF suites green.
