# A collapsed or edge-on transformed element still leaks links, widgets, the 3D compositor and perspective hairlines

An element whose `transform` leaves no visible area is not painted (see [the fix](../recent-fixes/2026-10-10-non-invertible-transform-paints-nothing.md)),
but four neighbouring cases are left as they were, all byte-identical to before that change:

- **Link annotations and form widgets ignore transforms.** `<div style="transform:scale(0)"><a href="...">` still emits an
  active `/Link` at the untransformed rectangle (`visibility: hidden` drops it; Chrome is not clickable), as does a link in a
  merely rotated element. Bookmarks and named destinations of a collapsed heading are kept.
- **A collapsed `transform-style: preserve-3d` root still reaches the 3D compositor**, because `TryPaintContext3D` runs before
  `LeavesVisibleArea` in `FragmentPainter.PaintFragment`: a transparent bitmap is emitted, and with `--pdfa=1b
  --flatten-transparency` it fails with "An effect PeachPDF renders as a bitmap...", like a visible `scale(0.5)` root.
- **Only the 2D matrix is checked.** Chrome treats a singular 4x4 (`scaleZ(0)`, `scale3d(1,1,0)`, a `matrix3d` with a zero
  z-row) as not rendered; PeachPDF projects to 2D first.
- **An edge-on plane under `perspective` can still draw a hairline** in PDFium: the projective warp has no thickness cull (the flat
  case does, `HasVisibleThickness`), and the text layer is pushed under the near-singular linearisation (determinant about 3e-8)
  around invisible `3 Tr` text. Harmless, and why `TryPushTransform` rejects only absolutely singular matrices.

Why they were left: each is a separate mechanism from the element's own `transform` guard (annotation collection, the 3D context
entry point, the 4x4-to-2D projection, the warp), none is a regression, and none aborts a render.

Filed as [issue #1703](https://github.com/jhaygood86/PeachPDF/issues/1703).
