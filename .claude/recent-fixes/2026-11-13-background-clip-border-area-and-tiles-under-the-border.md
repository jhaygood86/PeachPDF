# `background-clip: border-area`, and repeated tiles reach the border

- `border-area` (css-backgrounds-4): `ResolveClip` in `FragmentPainter.Decorations.cs` clips to the border box's rounded rectangle plus the
  padding box's own curve, filled even-odd. The page canvas (`isCanvas`) takes it as `border-box`: a root or body background has no border, and
  the ring would be empty. A `border-image` or `border-shape` is not in the ring (WPT `border-image`, `corner-shape`, `double`, `complex`... stay failing).
- Found on the way: a repeating image never reached the border. The repeat loops were bounded by the *positioning* area (the padding box by default),
  so with the default clip (border box) a transparent border showed no tiles. Bounded by the visible clip now (`visibleRect`, which already
  covers `fixed` layers); `space` continues its pitch outward the same way.
- Evidence: WPT css-backgrounds +9 on top of the SVG work, none lost across 16 modules; `LayoutPaintCoverageTests` render the ring, an image in it, and a
  repeat under a transparent border. Trap: a `data:` image needs `ResourceLoader` set on the context (`PageRaster.RenderAsync(loadResources: true)`).
