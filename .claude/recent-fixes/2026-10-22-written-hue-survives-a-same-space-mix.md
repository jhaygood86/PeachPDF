# A gray written in the mix's own space keeps its hue (#1775)

`color-mix()` converted every lab/lch/oklab/oklch operand through Oklab coordinates, which hold no hue, then decided a hue was powerless
(chroma <= 0.02, `ColorSpaceMath.IsHuePowerless`) and borrowed the other operand's. For `oklch(0.5 0 120)` in an oklch mix that is wrong:
the hue is the component as written (Chrome: 180 from 120 and 240).

`ParseMixOperand` now also returns the components as written (`ColorSpaceMath.WrittenCoords`), `Color.MixComponents` takes them from there when the
operand's space is the mix's, or its polar/rectangular twin (lab/lch and oklab/oklch share a white point so the conversion is exact), and
`ResolveMissingHues` no longer treats a hue that was written (not converted) as powerless. This also fixes a near-gray `lab()` (chroma 0.021-0.030)
that the Oklab round trip pushed under the threshold.

Found by running the issue's three cases first: the second (`oklab(.6 .001 0)` in an lch mix) already kept its own hue (3.9 degrees, chroma 0.34 in lch), so
the issue text was wrong about it - only the first and third needed the change. Mixing a hex gray is unchanged (still takes the other hue).

Not done: the animation colour token (`CssValueInterpolator`, the Oklab mix of an animated colour) still converts through Oklab.
