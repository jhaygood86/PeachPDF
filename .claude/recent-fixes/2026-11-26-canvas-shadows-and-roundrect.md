# Canvas shadows are a separate layer; roundRect takes elliptical radii

- A shadow is the shape's alpha recolored to the shadow color, offset in device space (no transform touches the offset), blurred with sigma = shadowBlur / 2, drawn first with the shape's alpha and composite operation. Backends cut a layer to the visible surface, so the shadow is its own layer with the offset applied inside it (a shape outside the canvas can still cast a shadow in).
- roundRect converts every argument first (WebIDL), then returns for non-finite rectangles or radii, throws RangeError for 0 or more than 4 radii or a negative radius, and builds each corner as an arc with its own horizontal/vertical radius. A negative width/height mirrors the outline, so the first radius is the corner at (x, y). Leaves a new subpath at (x, y).
- Measured: html-canvas 1687 -> 1807 files (2952/5030 subtests).
- Still open in Phase 4: stroker edge cases (empty/zero-length subpaths, round joins, scaled arcs), isPointInPath, strokeRect zero-size.

## Stroking
- A stroke prunes zero-length line/curve pieces and drops subpaths left with nothing to draw (lone moveTo, a point closed on itself) before drawing: no dot, however round the caps (`PathData.PrunedForStroke`).
- `strokeRect` with a zero width or height is a line out and back, closed (joins, no caps).
- `FlatPath.ForStroke` (raster stroker): an open subpath that starts/ends on a curve gets one extra point 1e-3 of the parameter from that end, so a cap is square to the curve's tangent rather than the last flattened chord (a wide arc stroke showed a visible tilt). The pinned `arc-ellipse-curves` scene hash moved for that reason.
- `isPointInPath` counts points on the edge as inside and validates the fill rule enum; `Finite()` now converts every argument first (WebIDL).
- html-canvas now 1835 files, 2980/5030 subtests. Open: closed A-B-A strokes need round joins at the 180 degree turns (`strokeRect.zero.4/.5`, `line.join.round`), thick self-intersecting arcs, `isPointInStroke` dashed at scale, miter-limit shadow join.
