# Win2D: two-circle radial gradients and anisotropic blur are approximated

- **Conic gradients** have no Win2D brush. They are painted as 360 aliased wedges clipped to the shape, as `SvgCanvas` does without
  `SvgConicGradientMode.Css`; the wedges are aliased because anti-aliased wedges leave faint seams where they meet.
- **Two-circle radial gradients** (`RadialGradientBrush.FocusRadius > 0`): `CanvasRadialGradientBrush` has a focal *point* (`OriginOffset`)
  but no focal *radius*, so the gradient starts from a point focus.
- **Anisotropic blur** (`BlurEffect.SigmaX != SigmaY`, the same for a drop shadow): `GaussianBlurEffect`/`ShadowEffect` take one standard
  deviation, so the mean of the two is used.
- **Repeating gradients** use `CanvasEdgeBehavior.Wrap`, which repeats the gradient over the segment between its end points; a gradient
  whose first and last stops do not span 0..1 is not re-based the way CSS does it. No document the showcase renders is affected.

Each is an approximation Win2D's API forces; none is a defect of the backend's own logic.
