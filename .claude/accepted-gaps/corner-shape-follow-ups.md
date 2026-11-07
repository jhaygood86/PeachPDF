# `corner-shape` follow-ups left out of the first implementation

Tracked in issue 2282. `corner-shape` and the shorthands around it work (see
[docs/html-css-support.md](../../docs/html-css-support.md)), with these deliberate deviations from CSS Borders 4:

- **Not animatable.** §3.9.6 maps the superellipse parameter onto a 0-1 range so bevel, round and square are evenly
  spaced, and interpolates that. A change of shape is discrete here.
- **Concave overlap (§3.9.5).** Only the plain corner-overlap factor (§3.3) is applied. A `scoop` or `notch` on diagonally
  opposite corners should also be scaled by the hull-based factor so the cuts cannot meet.
- **Padding-edge and content-edge curves of a concave corner.** `background-clip: padding-box`/`content-box` and the
  `overflow` clip shrink a scoop's or notch's radius by the border width as for a convex corner. The border's own inner
  contour does it the right way (the cut stays centred on the border box's corner and grows), see the recent
  fix for the corner shape work.
- **Region outlines** (a box split across fragments, or any outline that is not one rectangle) keep round corners.
- **`box-shadow` spread** grows a concave corner like a convex one.
- **A collapsed table** ignores the shape, as it ignores `border-radius`, see
  [border-radius-ignored-on-a-collapsed-table.md](border-radius-ignored-on-a-collapsed-table.md).
