# Bevel shade over-reach on rounded union outlines

Bevelled outlines (`inset`/`outset`/`groove`/`ridge`) on a wrapped inline painted
slivers and bars in the wrong shade wherever one boundary edge passed within about
half the outline width plus the corner radius of another edge's centreline: a light
sliver along a neighbouring line's outer top edge, a light bar across a staircase
notch's dark run, a half-light narrow edge. All three reproduce in both PDFium and
MuPDF, and all three are correct on `main` (per-line rings) and in Chrome.

## Load-bearing ideas

Three, layered in this order:

1. **Per-contour clips and fills.** The old code accumulated every contour's edges
   into one shared lit/shaded clip pair and filled one band path holding every
   contour. Any band pixel inside another contour's over-wide clip repainted in that
   edge's shade. `PaintBevelLayer` now loops contours: one lit/shaded clip pair and
   one band path per contour, so an edge can never reach another piece's band.
2. **Stadium clips instead of reach-wide quads.** The old quad carried
   `halfWidth + max(corner radius)` across the whole edge. The replacement carries
   half the width along the straight run and grows toward the radius only in caps
   around each end - and only on the arc's own side (inward for convex, outward for
   concave), because the diagonally-opposite quadrant holds no band of the edge.
   Cap reaches are the furthest of the layer's three fittings (centreline plus the
   band's own outer and inner contours, matched by index), since each contour fits
   its radii to its own edge lengths.
3. **Order-aware asymmetric cuts.** The lit shade paints first, so a lit territory
   reaching into a shaded band is repainted correctly by the shaded fill running
   after it - but a shaded territory reaching into a lit band is not. Shaded caps
   are therefore punched against every lit edge's straight strip (axis rects in
   layout space, subtracted before wedge clipping). Cutting never punches a hole:
   anything cut out of a shaded cap lay inside a lit strip, so the lit fill paints
   it. Lit territories are never cut.

## Found by running, not by reading

- A `FromLayout` copy-paste typo (`rect.Y0` where `Y1` belonged in the `Ny < 0`
  branch) silently dropped **every east-travelling edge's territory** - clips,
  coverage, all of it. It survived initial development because most assertions
  count subpaths per clip, and only the per-clip-count test (`ShadesByTravel...`,
  expected 3 got 1) caught it. An 8-branch coordinate mapping needs a test that
  exercises all four travel directions, which the suite now does on every run.
- Unscaled radii (tried to cover outer/inner arcs with pre-fit values) invert caps
  on short edges (`s0 > s1`) and reintroduce full-span over-reach. Fitted maxima
  over the three insets cover the arcs without ever inverting.
- Symmetric caps (tried for collapsed-ambiguity corners) reintroduce the
  diagonally-opposite quadrant. One-sided caps plus the order-aware cut were both
  needed; neither alone closes all three repros.
- The new test first asserted "no lit/shaded clip overlap anywhere" and flagged
  harmless overlaps (in union interior, or in the other shade's band where paint
  order resolves correctly). It now asserts ownership: a *lit-owned* straight-run
  pixel (nearest outer straight, mitre ties skipped) must not sit inside a shaded
  clip, and a *shaded-owned* one must sit inside a shaded clip. Arc samples are
  deliberately not probed - ownership there is ambiguous by construction.

## Deliberately not done

- Notch (concave) radii were not shrunk to stop arcs crossing into the next
  tread's band. That would change the band's shape (solid outlines too), not just
  its shading, with no Chrome evidence gathered here either way.
- Lit territories are never cut, and shaded straights never are either: both
  remaining overlap classes resolve correctly through paint order (same-shade
  union, or shaded-second-wins on shaded-owned pixels).
- No showcase addition: this restores intended behavior, it is not a new visible
  capability.

## Evidence

- Full suite: 12566 passed, 9 skipped, 1 failed (`Issue1157` table test, fails on
  unmodified `main` too).
- New tests: 4 repro cases. The staircase bar fails on the pre-fix code; the other
  three pass pre-fix at straight-sample resolution (their artifacts live in
  corner zones the straight probes skip) but lock the fixed geometry in - and all
  three repros were verified fixed by rasterizing old-vs-new through both
  engines.
- Coverage: every new/changed line hit; the only uncovered lines in the file are
  the pre-existing documented-unreachable `FitClosed` fallback.
- Rasterized all three issue repros old-vs-new through **both PDFium and MuPDF**:
  old shows the light bar across the dark notch run (plus a light sliver and a
  dark triangle on the groove repro); new is clean in both engines, agreeing with
  each other.
