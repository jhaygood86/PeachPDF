# Patterned and bevelled outline styles union across a wrapped inline's lines

Closes upstream #1206, the gap deliberately left by the union work that landed in `c0fd2aca`. All six
remaining outline styles — `dotted`, `dashed`, `groove`, `ridge`, `inset`, `outset` — now paint the
merged region a wrapped inline's lines cover, instead of falling back to one closed ring per line.
`OutlineDrawHandler.SupportsRegionOutline` is the single gate, now `is not (None or Hidden)`.

## The gap file was wrong on both of its load-bearing claims

Worth knowing, because it is what made this cheap rather than expensive:

1. **It said the required primitives had to be invented.** Both already existed.
   `StyledStrokeFitting.FitClosed` does closed-path dash fitting, and `BorderBevelColors.ForSide`/
   `ForSegment` already produce the contrast-aware light/dark faces. Nothing new had to be built at
   the drawing layer — the feature is geometry plus dispatch.
2. **It assumed Chromium degrades these styles too**, and that the right fix was therefore to
   document a matching degradation. It does not. Blink's `ComplexOutlinePainter` handles every
   non-`auto` style on the unioned region with `NOTREACHED()` as its default arm, and only takes a
   simple path when the union *is* the first rect. So the gap was closed with real behavior.

Check the claims in a gap file before budgeting from them; this one had never been run down.

## The load-bearing idea: a mitre clip is one edge's territory, bounded by two 45-degree lines

Each boundary edge of the region gets a quad accumulated into its shade's clip path. Measuring `s`
along the edge from its start and `h` outwards from its centreline, its territory is bounded by one
45-degree line per end, and the whole of the geometry is in where those two lines sit and where the
quad stops.

It took three attempts to get right, and the two failures are worth more than the result, because both
shipped past a full green suite and both drew as *the same symptom*: a clean unpainted diagonal slash
across the band, at the notch a merge introduces.

**"The mitre consults neither neighbour" is false.** The first version leaned every mitre the convex
way, reasoning that a corner's bisector is the same line whichever way the boundary turns there, so
both edges would cut it identically and abut with no case analysis. The premise is true and the
conclusion does not follow: the two edges take *opposite sides* of that line depending on the turn. A
convex corner is the outside of the turn and the band there is longer on the outward side, so the
mitre opens outwards (`s = -h`, `s = L + h`); a reflex corner is the inside and each mitre leans the
other way (`s = h`, `s = L - h`). Lean them all the convex way and the inner half of every reflex
corner is in neither edge's clip. Square-cornered cases hide this completely - with no arc to carry,
the quads meet at the corner point either way - which is why it survived a suite that was otherwise
quite thorough about the notch. So the quad now takes one bit per end: whether that corner is convex.

**A mitre must also reach *past* the straight run.** Only the two lines have to be exact; how far the
clip runs along them need not, since `BuildBandPath` bounds the paint. That slack is load-bearing,
because near a rounded corner the band is an annular sector deviating from the straight centreline by
up to the corner's radius. A clip only `halfWidth` deep leaves a wedge of every arc outside every clip
- 5,652px of it (PDFium, scale 6) on `border-radius: 30px` with a 6px `inset` outline. The reach
carries the corner radius too.

**And then the two mitres cross.** Nothing prevents it: they are cut from the edge's own line, and
where they lean towards each other they meet at a finite depth. Past that crossing the territory has
turned inside out, so a quad drawn to a fixed depth regardless folds into a bowtie whose waist cancels
under `RFillMode.Nonzero`. It bites on any edge shorter than twice the reach - and since the reach now
carries the radius, a `border-radius: 12px` already exceeds the ~10pt steps of a wrapped inline's
staircase, which is to say it bites on the ordinary case. The depth is cut off at the crossing.

This is still simpler than Blink's `MiterSlope` + inverse-winding construction, but it is *not* the
neighbour-free rule the first version claimed.

## The clips come from the rectilinear region, not from the rounded path

This was got wrong first, and the first version shipped past its own tests. Clipping was originally
derived from `BuildContourSegments` - the same rounded segment run the band is *drawn* along - with a
`(dx == 0) == (dy == 0)` guard meant to drop zero-length segments. A corner **arc** has both deltas
non-zero, so that guard is true for every arc and silently skipped it. Since the band is painted only
through the clips, a `border-radius` + bevelled outline came out almost entirely unfilled, with stray
triangles where the straight runs were. That is a regression: before the gate was broadened those
styles fell through to `BoxEdgesDrawHandler.DrawRoundedSideBand`, which handles rounded bevels.

Taking the clips from `RectilinearRegion.Shrink(contour, halfWidth).Points` instead fixes it
structurally rather than by special-casing arcs: an arc has no direction of travel and so no shade of
its own, and the region's right-angled corner gives it two mitres that meet on its own 45-degree
diagonal, handing each half of the arc to the edge it runs into. That is also how a rounded bevelled
border is shaded on an unfragmented box.

Three things let this ship broken: no test covered rounded + bevelled, the two showcase bevel swatches
had dropped their `border-radius`, and the existing test asserted *one clip per fill* - which the
broken version satisfied. All three are fixed; the new test was confirmed to fail on all four styles
when the clips are taken from the path.

## One fill per shade, not one per edge

The first version also pushed a clip and filled the band once **per edge**, including for consecutive
edges resolving to the same shade (clockwise, left-then-top are both `isTopOrLeft`). That abuts two
same-coloured antialiased fills along each mitre, reintroducing the seam
`.claude/invariants/paint-two-abutting-antialiased-fills-leave-a-seam-along-their-shared-edge.md`
documents and `BoxEdgesDrawHandler.TryDrawSeamlessBevel` exists to avoid - confirmed visually as a
pale diagonal at 6x zoom, with a pixel histogram showing intermediate colours alongside the face's
own. Accumulating every same-shade quad into one clip and filling once per shade removes the seam and
drops the draw calls from O(edges), each re-emitting the whole band's geometry, to exactly 2 (4 for
`groove`/`ridge`'s two passes). `BuildBandPath` was split out of `FillBand` so the band's geometry is
built once per layer and reused across both clips.

## Deliberately not copied from Blink

Blink degrades `double` at width ≤ 2 to solid, and `groove`/`ridge` at width == 1 to a 50%
dark-mixed solid. Not implemented here: grepping `BoxEdgesDrawHandler.cs` turned up **no**
width-based style degradation on the per-side border path either, so adding it only to the region
path would make the two disagree with each other. Blink's complex path also uses raw `color_`/
`color_.Dark()`; this uses `BorderBevelColors.ForSide`, which is contrast-aware, for consistency
with this repo's own per-side path.

## Evidence

- **Solid-vs-bevel mask diff is the decisive test, and the only thing that found either slash.**
  Render the *identical* shape once as `solid` and once as the bevel: `solid` paints the true band, so
  any pixel solid paints and the bevel leaves pure white is an uncovered wedge, with no judgement call
  about what the shape should have been. On the showcase swatch that read **1121px before, 75px after**
  (PDFium, scale 6), largest remaining cluster 7px.
- Always cluster the differences before reading them. Thirty-odd one-to-three-pixel clusters are
  antialiasing straddling the threshold and are expected; a single cluster of ~50px or more is real.
  Classify a painted pixel as `b > r + 20 and b > 100` for a blue outline - a plain near-white test
  falsely counts the red border and the black text.
- A pale one-pixel column at the band's own outer edge will show up as a ~48px "cluster" under that
  blue test while being perfectly painted; checking for *pure white* instead distinguishes a real hole
  from an antialiased boundary. Both were checked.
- Swept width × radius × style (2/3/6px × 0/10/12/30/60px × all four bevels) through **PDFium and
  MuPDF**: zero truly-unpainted pixels in every case, square and rounded.
- Screenshotted the same fixtures in **real headless Chrome** and compared the notch at 6x.
- Falsifiability proven three times: reverting `SupportsRegionOutline` made all 8 original cases fail
  with 12 edges; taking the bevel clips from the rounded path made all 4 rounded cases fail; removing
  the bowtie cut-off made all 4 convexity cases fail.
- 235 outline tests pass; full suite green apart from the pre-existing `…_Issue1157` table failure.

## Why three green suites in a row missed a hole in the middle of the band

Each successive test was a genuine improvement and each was still blind to the next defect. Worth
knowing before trusting a new assertion here:

- **"One clip per fill"** is satisfied by a clip containing the wrong geometry entirely - it passed
  against a rounded bevel that painted almost nothing.
- **Clip bounds (min/max X/Y)** are satisfied by the straight edges' own quads whether or not anything
  covers the arcs between them. This is what was blind to the 5,652px corner wedges.
- **Point-in-polygon coverage** of the flattened band finally caught the reflex-lean defect - but is
  *structurally incapable* of catching the bowtie, because a ray cast from inside the cancelled waist
  still crosses the bowtie's outline an odd number of times, so it reports covered. That one needs an
  assertion on the shape (`SelfIntersects`), not on coverage.

The general lesson is the repo's existing one, sharpened: a clip is not evidence of what gets painted.
Only the rasterized result is, and a mask diff against the same shape drawn `solid` turns that into a
number rather than a judgement.

## Traps that cost time

- **Union topology arithmetic.** Three lines of *differing* width union to a staircase (8 edges),
  not a rectangle, and an **L-shaped union is a hexagon (6 edges)**, not 8. Both of my first
  expectations were wrong and the code was right both times — when a paint-sequence count surprises
  you here, draw the contour before editing the painter.
- `BuildSegments` opens a square-cornered run with a **zero-length segment**, and a union can close
  a contour onto its own start. `StrokeStraightPattern` skips anything where `horizontal ==
  vertical`; without that it emits bogus strokes.
- **Two coordinate conventions in one file.** Path geometry bypasses the adapter's scaling and needs
  `/ pixelsPerPoint`; `DrawLine` coordinates do not. Dash arrays and pen widths reach the backend
  undivided and must be converted.
- The showcase row overflowed its page once it held six swatches — caught only by rasterizing the
  regenerated PDF, not by any test. It is now two rows of four.
- **A showcase swatch that drops the property under test stops being evidence.** The two bevel
  swatches had no `border-radius`, so regenerating and eyeballing the showcase - the step that has
  caught several rendering bugs here - could not have shown the broken rounded bevel. They have it
  back.
