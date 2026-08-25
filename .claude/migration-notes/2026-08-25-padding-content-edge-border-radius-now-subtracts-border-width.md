# Padding-/content-edge `border-radius` curves now subtract border width (and padding)

Previously, a `background-clip: padding-box`/`content-box` fill, and the descendant clip curve of a
rounded `overflow: hidden` box, both curved the smaller (padding- or content-edge) rectangle using
the box's raw, declared `border-radius` — the same value used for the border-box curve itself. For a
border thick relative to its radius (e.g. `border: 6px solid; border-radius: 14px`), this made the
inset curve bulge past the border's own inner edge: a visible gap/notch appeared between the border
stroke and the fill or clipped content it enclosed, at every corner.

The inset curve's radius is now the border-box radius minus the border thickness on that corner's
adjacent edges (and, for the content edge, the padding too), clamped to zero, per
[CSS Backgrounds and Borders Module Level 3 §5.5](https://www.w3.org/TR/css-backgrounds-3/#corner-clipping)
("the padding edge (inner border) radius is the outer border radius minus the corresponding border
thickness"). A `border: 6px solid; border-radius: 14px` box's padding-box background (or a rounded
`overflow: hidden` box's clip curve) now uses an effective radius of `14 − 6 = 8px`, matching the
border's own inner edge. When the border (or border+padding, for the content edge) is wider than the
declared radius, the inset curve is now a plain rectangle (radius clamped to zero) rather than
retaining the outer curve's rounding.

This changes rendered output for every existing box that combines a `border-radius` with a
`padding-box`/`content-box` background clip, or with `overflow: hidden`, and has a non-trivial
border/padding relative to its radius. A box with no border (or with a radius large relative to its
border) sees little or no visible change, since the reduction is either zero or small.
