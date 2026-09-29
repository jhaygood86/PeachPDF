# SVG: rotated radial gradients, arc markers, filled polylines

- A radial gradient with a rotating/skewing `gradientTransform` used to render as an axis-aligned ellipse; it is now a true rotated ellipse.
- Markers on elliptical-arc path segments used to orient along the arc's chord; they now follow the arc's true tangent.
- A `<use>` referencing another `<use>` of a container with `opacity` used to double-blend overlapping children; it is now one isolated group.
- Filled `<polyline>` is filled as a closed shape on backends that need the explicit closure (no change in PDF output).
- `PeachDrawing.Core`: `RadialGradientBrush`, `RenderContext.GetRadialGradientBrush` and `Canvas.GetRadialGradientBrush` take an optional trailing `Matrix3x2? transform`.
