# Forced colors drops gradient backgrounds and recolors inline SVG paint

Follow-up to [the first forced-colors change](2026-11-02-forced-colors-and-font-scale.md) (CSS Color Adjust 1 section 3.1).

- Gradient layers (linear, radial, conic) are skipped in `FragmentPainter.Decorations` when `ForcedColorsAdjuster.DropsDecorations(box)`; `url()` layers are kept and the forced background color still paints under them.
- `ForcedColorsAdjuster.RecolorSvg` runs once in `CssBoxSvg.EnsureDocument` after the scene graph is built and replaces every visible solid, gradient or pattern fill/stroke with `CanvasText`. Because gradient and pattern paint is replaced, `stop-color` needs no override of its own.
- Not done: an `<img src="*.svg">` is an image and keeps its colors (as in browsers).
- Evidence: `ForcedColorsTests` (gradient dropped, SVG recolored); PeachBrackets.Tests and PeachPDF.Tests green on net8.0.
