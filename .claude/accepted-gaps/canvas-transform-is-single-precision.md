# Canvas 2D: the transform is single precision

`isPointInPath` after `ctx.scale(Number.MAX_VALUE, Number.MAX_VALUE)` answers false where HTML answers true for a point inside the path
(`html/canvas/{element,offscreen}/path-objects/2d.path.isPointInPath.basic.html`, last subtest).

The context keeps its current transform as a 32-bit `Matrix3x2` and stores each path point already carried through it, so a scale that
overflows a float turns the path into infinities. The specification works in double precision and tests the point in the path's own space.
Fixing it needs a double-precision matrix in the canvas state and path points kept in user space, which belongs with moving the path model
into `PeachDrawing.Core` rather than a patch to the hit test. Tracked in issue 2495; both files are listed in
`wpt-expected-failures.json` until it closes.
