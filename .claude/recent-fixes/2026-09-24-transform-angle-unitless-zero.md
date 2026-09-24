# Transform functions accept a unitless zero angle (#1327)

`transform: rotate(0)` (and rotate3d/X/Y/Z, skew, skewX/Y) failed the CSS-OM `TransformProperty` grammar,
so the whole declaration was dropped. CSS Transforms 1/2 give those arguments as `[ <angle> | <zero> ]`.

**The fix is deliberately local.** `Converters.TransformAngleConverter` (`ValueExtensions.ToAngleOrZero` +
calc) is used only by `RotateTransformConverter`/`SkewTransformConverter`. The shared `AngleConverter`
stays strict: a unitless zero is invalid for a general `<angle>` (CSS Values 4 §6.1), and it also backs
`font-style: oblique <angle>` and the gradient grammars. `ToAngleOrZero` returns `0deg`, so the serialized
form is `rotate(0deg)`, as in browsers (`Angle.Zero` is `0rad`).

Also fixed in passing: `skew()`'s second argument was `Required()`; the spec makes it optional, so
`skew(10deg)` was rejected too (`skew(0)` is in the issue title).

**Traps / findings**
- The render-side path never had the bug: the box-level `transform-list` validator
  (`CssValueParser.IsSyntacticallyValidTransformList`) doesn't inspect arguments and `BuildFunctionMatrix`'s
  `AngleArg` already resolves a non-Dimension argument to 0. The drop only showed through the CSS-OM parse
  (`TransformProperty` / `Converters.TransformConverter`).
- The issue's "visible effect" (an `overflow:hidden` + `transform: rotate(0)` wrapper failing to clip an
  absolutely positioned descendant) is **not** reproducible: `none`, `rotate(0)` and `rotate(0deg)` all
  rasterize identically (PDFium, clipped to 80x50px). `IsTransformed` is `!ActualTransformMatrix.IsIdentity`,
  so an identity transform forms neither a stacking context nor a containing block regardless of how it
  is spelled; do not treat this fix as having changed that.
