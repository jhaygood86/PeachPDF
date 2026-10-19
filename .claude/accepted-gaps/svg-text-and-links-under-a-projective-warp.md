# SVG text is not selectable, and links are approximate, inside an element under a projective 3D transform

An SVG element drawn through a real projective map (a `perspective` on its container, or `perspective()` in its `transform`) is painted into a bitmap and warped (`SvgRenderer.Projective.cs`, over the shared `ProjectiveRaster`). Two things inside it do not follow the way they do for an HTML box:

- Text drawn into the bitmap is not supplied again as invisible text, so it is not selectable. `FragmentPainter.SupplyWarpedText` does that for box text; SVG text has its own paint path (`PaintInvisibleText`), and no text-only pass over a warped subtree exists. The same already holds for SVG text inside a raster filter.
- A link collected from an `<a>` inside the element covers the bounding box of the element's *orthographic* projection (`SvgElement.Transform`, the plain 2D matrix of the 3D one), because links are collected by a separate walk that knows nothing of the projective map. Under strong perspective that box can sit away from where the link is drawn.

An affine result (a rotation with no perspective, or a perspective so faint it is within a quarter of a device pixel of one) is vector, so text stays text and links are exact there; only the bitmap case has the gap. Tracked as #1812.
