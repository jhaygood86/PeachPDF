# Canvas shadows are a separate layer; roundRect takes elliptical radii

- A shadow is the shape's alpha recolored to the shadow color, offset in device space (no transform touches the offset), blurred with sigma = shadowBlur / 2, drawn first with the shape's alpha and composite operation. Backends cut a layer to the visible surface, so the shadow is its own layer with the offset applied inside it (a shape outside the canvas can still cast a shadow in).
- roundRect converts every argument first (WebIDL), then returns for non-finite rectangles or radii, throws RangeError for 0 or more than 4 radii or a negative radius, and builds each corner as an arc with its own horizontal/vertical radius. A negative width/height mirrors the outline, so the first radius is the corner at (x, y). Leaves a new subpath at (x, y).
- Measured: html-canvas 1687 -> 1807 files (2952/5030 subtests).
- Still open in Phase 4: stroker edge cases (empty/zero-length subpaths, round joins, scaled arcs), isPointInPath, strokeRect zero-size.
