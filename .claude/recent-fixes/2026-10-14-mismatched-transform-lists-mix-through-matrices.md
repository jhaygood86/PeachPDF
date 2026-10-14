# Mismatched `transform` lists mix through matrix decomposition (animation still frame)

Tracked in #1752, under #1672 (remaining gaps: #1753). `CssValueInterpolator` only mixes two values of the *same shape*; a transform pair of different shape
fell to the discrete flip. Now, when that is where a `transform` pair would land, `Interpolate` composes both lists to one
`Matrix4x4` each and `TransformMatrixInterpolator` decomposes, mixes and recomposes them, writing `matrix()` (both flat) or
`matrix3d()` back as text for the ordinary setter.

## The load-bearing ideas

- **Pairing comes before matrices, and the matrix is only the tail.** CSS Transforms 1 §11 pads the shorter list with
  identity functions and walks the two in step while the functions at a position share a primitive (§12: `translateX` with
  `translate`, `rotate` with `rotateZ`, an argument-short `scale(1)` with `scale(2, .5)`); each pair is mixed on its own,
  and only at the first position with no common primitive is what is left of each list composed to one matrix (§13). An
  earlier version of this change matrix-mixed the *whole* list on the first mismatch, which composes `rotate(360deg)` to the
  identity - the common spinner `rotate(0deg)` to `rotate(360deg) scale(1.2)` stopped spinning, and `rotate(0deg)` against
  `rotateZ(270deg)` went the wrong way. `CssValueInterpolator.TryMixTransformLists` builds the paired prefix as text (canonical
  primitives only where the two spell it differently, so `translateX(5px)` stays `translateX(5px)`), mixes it with the
  ordinary same-shape rule, and appends the tail as `matrix()`/`matrix3d()`. `rotate3d` pairs only on one axis direction.
  `matrix()` and `matrix3d()` never pair: they are always the matrix tail. `perspective()` is deliberately *not* in that set: a
  list holding one used to mix entry by entry (the spinning card, `perspective(600px) rotateY(0)` to `... rotateY(180deg)`), and a
  matrix tail cannot decompose perspective, so a perspective that did not pair would have turned that into a flip (the third review caught
  it). Two `perspective()` calls pair and mix their lengths; one against another function flips.
- **No second transform grammar.** Composing a list is `CssValueParser.ComposeTransformFunctions`, the loop that used to sit inside
  `BuildFinal4`, so paint and animation read the one `BuildFunctionMatrix`. That needs a `CssBox` (lengths: `em`, `rem`, `vw`), which is why `Interpolate` gained an optional `box` and `AnimationApplier.Sample` passes it. Without a box the pair
  flips, as before.
- **A percentage or a container unit cannot be mixed here.** The sampler runs in the cascade, before layout, so `ActualWidth` is 0 and
  `translateX(50%)` would silently resolve to 0. Any `%` or `cq*` in either list keeps the flip (a container query unit resolved against a container with no size gave a silent 0 - the
  fifth review caught it). Do not "fix" that by composing later:
  the box's matrix is built lazily at paint (`DerivedStyle.ActualTransformMatrix`), so a percentage would need a symbolic
  decomposition part.
- **Layout-unit points in, CSS pixels out.** `BuildFunctionMatrix` returns translations in points (and runs `matrix()` through
  `PixelMatrixToPoints`); the emitted `matrix()` text is parsed back through that same path, so the translation is divided by
  `Length.PointsPerPx` on the way out. The 100px/75pt unit tests pin it.
- **Row-vector convention.** `Matrix4x4` composes `S · K · R · T` where CSS writes `T · R · K · S`. The spec's pseudo-code indexes
  `matrix[column][row]`, which is the same layout as `Matrix4x4`'s `M(row)(col)`, so it transcribes directly; a decompose/
  recompose round trip over a table of matrices (including mirrored ones) is the test that would catch a transposition.
- **Mirrors.** 2D: the flip is carried by one scale (so the rotation stays an angle); a mirror in x on one side and in y on the
  other is first turned a half turn, or the mix would pass through a zero scale (§13.2.2). 3D: all three scales and axes are
  negated, which leaves the skew ratios unchanged.
- **A flat side meeting a spatial one is re-decomposed spatially** (`TryDecompose(..., allowFlat: false)`), so a mirror is carried
  by all three scales on both sides; the 2D mirror is carried by the x scale when `m11 < m22`, else the y scale (the spec's choice).
- **Quaternion path takes the short arc** (`Quaternion.Slerp` flips a negative dot); this was chosen over the spec's literal
  formula, which would go the long way for a negative dot, and was not checked against a browser.

## Not done

Font-relative lengths bake at the animation step on the matrix path (the gap note has the case); bailing on them would have
lost the common `translateX(2em)` pair to the flip.

Perspective is not decomposed (the spec carries it as a fifth part): a `perspective()` against another function flips, and two of
them mix their lengths where the spec mixes reciprocals. A symbolic percentage part is
not attempted (see above). The pairs that still flip are in
[the accepted-gap note](../accepted-gaps/css-animation-snapshot-limitations.md).

## Evidence

Full net8.0 suite passes (17,326 passed, 9 skipped, 0 failed); every new and changed executable line of `TransformMatrixInterpolator` and
`CssValueInterpolator` is covered by that run; whole-solution `-t:Rebuild` has 0 warnings and 0 errors. The `css_animation` showcase's
`shuffle` row (`rotate(0deg)` to `translateX(240px) rotate(90deg) scale(1.6)`, a 45 degree diamond at the midpoint) was rasterized in
PDFium and MuPDF and the two agree. Five sequential review rounds each found something real: the whole-list matrix mix that lost full
turns, primitive pairing and the 2D mirror fix-up, the `perspective()` regression, and the `cq*` silent zero.
