# Two abutting antialiased fills leave a seam along their shared edge

Two filled shapes that share an exact edge do **not** composite to full coverage there. Each covers
part of the boundary pixel, each is blended against the backdrop separately, and the result is a pale
hairline of backdrop showing through the join. It is a property of antialiased compositing, not a
rounding bug, and no amount of coordinate precision removes it.

The measured symptom: a plain `border: 4px solid` painted as four mitred quads showed a visible light
diagonal at all four corners, against a browser's uniform fill. It is invisible when the two shapes
differ in color — the join is meant to be seen there — and obvious when they are the same color.

So: **when adjacent shapes share a color, paint them as one shape, not two that touch.**
`BordersDrawHandler.TryDrawUniformBorder` does this for a border whose four sides agree, filling one
closed ring (outer and inner rectangular subpaths, even-odd) per band instead of four quads.

Two things to keep true if this is ever revisited:

- **Fill once, do not overlap.** Making the two shapes overlap instead of abut also hides the seam,
  but double-paints the overlap — which shows immediately with a translucent color, and under an
  active blend mode, where each drawing operation composites separately.
- **Only merge what genuinely shares a color.** `inset`/`outset`/`groove`/`ridge` shade each side
  differently on purpose (see `BorderBevelColors`), so they have no single color to merge into, and
  they are exactly the cases where the seam does not show anyway.

A border painting as one ring rather than four quads is also a **test observable**: several tests
counted per-edge polygons as a proxy for "the border painted", or for "this fragment closed itself
off" under `box-decoration-break`. Read `TestRecordingGraphics.FilledShapes`, which reports a fill
whichever primitive produced it, rather than `DrawPolygonCall` alone.
