# `transform-style: preserve-3d` is not modelled: every element is flat

`perspective`, `perspective()` and 3D transforms on a single element render in real perspective (see
[html-css-support.md](../../docs/html-css-support.md#3d-transforms-and-perspective)), but each element is warped as a flat plane on its own.
`transform-style: preserve-3d` - nested planes sharing one 3D space, depth-sorted and intersecting each other (a cube of six faces, a
carousel) - needs a real 3D scene graph and per-pixel depth resolution (a z-buffer, or BSP splitting of intersecting planes), which the
paint pipeline (a forward-only walk of a 2D fragment tree, one element at a time) has no place for. Painting the planes back to front
by their transformed depth would cover the common non-intersecting case, but not the intersecting one, and would need the transform of
an element's ancestors carried down into the warp rather than applied per element.

Not done, and nothing approximates it: `preserve-3d` behaves as `flat`, and a `perspective` reaches only the direct children of the
element that sets it (a grandchild is flattened into its parent, as `flat` says).

**Tracking issue:** #1311 (CSS Transforms 2 sections 6.1 and 9, 3D rendering contexts).
