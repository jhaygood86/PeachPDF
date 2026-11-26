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
- html-canvas then 1835 files, 2980/5030 subtests.

## Stroker leftovers (finished)
- A turn straight back on itself (cross product 0, dot < 0) has no "outside" to pick from the cross sign; `Stroker.AddJoin` now draws a round join as the half disc the path was heading into and nothing for miter/bevel. A *closed* two-point subpath is a real closed contour (line out and back: joins, no caps), where it used to be treated as open and capped (`closed && Count > 2` became `> 1`).
- A miter limit under 1 bevels every join (it used to fall back to 10); 0 still means "never set".
- `FlatPath.ForStroke(path, tolerance, halfWidth)`: each chord is stroked as a rectangle, so a chord's turn moves its ends by halfWidth * angle. When the stroke is wide against the curve (a 200 px stroke on a radius 25 arc) the cubic is cut so no chord turns more than 2 * tolerance / halfWidth (cap 4000), otherwise the rectangles near the circle's center overlapped into the wedge the arc must not cover.
- `arc()` between two angles a whole turn apart against its direction (0 to 2*pi counterclockwise, 2*pi to 0 clockwise) is a whole circle (what browsers draw), not no arc; equal angles are still no arc.
- `isPointInStroke` no longer approximates: it strokes the path in user space with `PathStroker` (the shared stroker, so dashes, caps, joins and a non-uniform scale) and tests the point against the outline with the nonzero rule. `Touches` is gone.
- Open: `isPointInPath` after `scale(Number.MAX_VALUE, ...)` (single-precision transform; issue 2495, accepted gap, expected failure).
- html-canvas now 1849 files, 2994/5030 subtests; floor 2984. PeachDrawing.Tests (441), PeachPDF.Tests, PeachBrackets.Scripting.Tests and PeachDrawing.PDF.Tests pass, with the pinned parity hashes unchanged.
- Native backends (Skia, Win2D, CoreGraphics) stroke with their own engines and were not changed; they were not checked against these cases (CoreGraphics cannot be built here).

## Phase 5: layers and filters
- `beginLayer`/`endLayer` are a stack of frames in `CanvasBitmap`: what is drawn while a layer is open goes to the frame's own op list and reaches the canvas (or the enclosing layer) as one composited op at `endLayer`, which `RecordWith`s the state the canvas had at `beginLayer` (alpha, operator, shadow, filter, clips; identity transform because each op carries its own). Inside, alpha/operator/shadow/filter start over. A `restore()` cannot pop a save made before the layer, `endLayer()` throws with no layer or a save still open inside it, `reset()` drops the layers, and everything that reads the canvas back (getImageData, putImageData, toDataURL, toBlob, convertToBlob, createImageBitmap, createPattern, drawImage from it) throws InvalidStateError while one is open. A promise-returning method rejects instead of throwing.
- `PeachDrawing.Core.FilterList` is the CSS filter-list grammar (Core has no CSS parser, so a color reader is passed in) and `ColorMatrix` gained the Filter Effects matrices as public statics; `FilterEffectResolver` in the engine now calls them rather than keeping its own copies. `ctx.filter` stores the text as written (`blur(  5px)` round-trips), ignores anything not valid, and a filtered drawing is drawn in a layer with the effects, inside the shadow and the alpha layer (filter, then shadow, then alpha/composite).
- `CanvasFilter` validates the primitives the way Chrome does (a present member must convert to a finite number, undefined included; unknown names are ignored) and applies gaussianBlur, colorMatrix and dropShadow. componentTransfer, convolveMatrix and turbulence are validated but not yet applied (they need Core layer effects and backend implementations), and `url()` SVG filters are not read.
- html-canvas 1903 files, 3058/5030 subtests; floor 3048.
