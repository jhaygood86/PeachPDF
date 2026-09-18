# A unioned bevel's clips come from the rectilinear region, not from the path it paints

`OutlineRegionPainter.Styled.cs` paints a unioned outline's bevel by filling the **whole band** twice,
once per shade, each fill clipped to the edges that resolve to that shade. Those clips are built from
`BuildContourCorners` - the region's own right-angled corner points, with each corner's resolved arc
radii - and **not** from the rounded path the band itself is drawn along. Several rules follow, and
every one of them has already been paid for once.

## The clip quads come from right-angled edges only

A rounded corner is emitted as a `PathSegment.Curve`, which has no single direction of travel and
therefore no shade of its own. Derive the clips from the rounded path and every arc is silently
skipped by the `(dx == 0) == (dy == 0)` zero-length guard - an arc satisfies it, since both deltas are
non-zero. The rectilinear corner points instead give each corner two mitre quads that meet on its 45
degree diagonal, so the arc is split between the two edges it joins, which is also how
`BoxEdgesDrawHandler.DrawRoundedSideBand` shades a rounded bevelled border on an unfragmented box.

**Symptom if violated:** a `border-radius` + `groove`/`ridge`/`inset`/`outset` outline on a wrapped
inline paints an almost entirely unfilled band with stray triangles where the straight runs were -
grossly broken, not subtly wrong.
`RoundedBevelledOutlineOnAWrappingInlineElement_ShadesTheWholeBandIncludingItsArcs` is the guard; it
was confirmed to fail on all four styles when the clips are taken from the path instead.

## A mitre must reach past the straight run, by the corner's radius

Only the two 45-degree lines have to be exact; how far the clip runs along them need not, because it is
`BuildBandPath` that bounds the paint. That slack is load-bearing: near a rounded corner the band is an
annular sector that deviates from the edge's straight centreline by up to the corner's radius, so a
clip only `halfWidth` deep leaves a wedge of the arc outside every clip. The reach therefore carries
the corner radius too.

**Symptom if violated:** an unpainted wedge at each rounded corner - 5,652px (PDFium, scale 6) on a
`border-radius: 30px`, 6px `inset` outline, against 35px of antialiasing once fixed.

Because a coverage test that only checks the clips' min/max extent is blind to this - the straight
edges' own quads already reach that extent - the guard flattens the band's Béziers and asserts each
sampled point falls inside some clip.

## Each shade is one fill, not one fill per edge

Consecutive edges frequently resolve to the *same* shade (travelling clockwise, left-then-top are both
`isTopOrLeft`; right-then-bottom are both not). Filling each edge separately abuts two same-coloured
antialiased fills along their shared mitre, which is exactly the artifact
[paint-two-abutting-antialiased-fills-leave-a-seam-along-their-shared-edge.md](paint-two-abutting-antialiased-fills-leave-a-seam-along-their-shared-edge.md)
describes and that `BoxEdgesDrawHandler.TryDrawSeamlessBevel` exists to avoid on the per-side path.
Accumulating every same-shade quad into one clip path also drops the draw calls from O(edges) to 2
(or 4 for `groove`/`ridge`'s two passes), each re-emitting the band's geometry once rather than once
per edge.

**Symptom if violated:** a pale diagonal hairline runs out of each corner of the outline, visible at
about 6x zoom against a dark bevel; a pixel histogram of the corner shows a spread of intermediate
colours alongside the face's own.

## A mitre leans the way its corner turns, and stops where the two mitres cross

Each quad is derived from the edge's own direction and outward normal `(dy, -dx)`, plus **one bit per
end: whether the corner there is convex**. Measuring `s` along the edge and `h` outwards from its
centreline, a convex corner is the outside of the turn, so the band is longer on the outward side and
the mitre opens outwards (`s = -h` at the start, `s = L + h` at the end); a reflex corner is the inside
of the turn and its mitre leans the other way (`s = h`, `s = L - h`). Both edges meeting at a corner
read that same bit, so they still cut it along one shared line and abut exactly - the point is only
that *which* line that is depends on the corner, not on the edge.

It is tempting to conclude it does not: the bisector of a corner is geometrically the same line
whichever way the boundary turns, and a first implementation here leaned every mitre the convex way on
exactly that reasoning. It is wrong, and square-cornered test cases will not show it, because with no
arc to carry the quads meet at the corner point either way.

**Symptom if violated:** on a wrapped inline with `border-radius`, the inner half of every reflex
corner - the notch between two lines - falls in neither edge's clip, drawing as an unpainted diagonal
slash across the band. Measured at 1121px (PDFium, scale 6) on an 8pt `groove` over three lines, against
75px of antialiasing once fixed.

Blink's `ComplexOutlinePainter` reaches the same place by a different route (`MiterSlope` plus inverse
winding); this engine's rule was cross-checked against real Chrome and against both PDFium and MuPDF.

## The clip must stay a simple polygon

Nothing about the two mitres stops them from crossing: they are cut from the edge's own line, and where
they lean towards each other they meet at a finite depth. Past that crossing the edge's territory has
turned inside out, so a quad drawn to a fixed depth regardless folds into a **bowtie**, and its waist
cancels under `RFillMode.Nonzero`. The depth is therefore cut off at the crossing, which is where the
territory genuinely ends.

This bites whenever an edge is shorter than twice the mitre's reach - and the reach carries the corner
radius as well as half the width, so a `border-radius` of 12px already exceeds the ~10pt steps of a
wrapped inline's staircase.

**Symptom if violated:** another unpainted diagonal slash, in the same places and for a different
reason - which is why the guard is not a coverage test. A ray cast from a point inside the cancelled
waist still crosses the bowtie's outline an odd number of times, so point-in-polygon coverage reports
it as covered. `BevelledOutlineOnAWrappingInlineElement_KeepsEachClipConvex` asserts on the shape
instead, and was confirmed to fail on all four styles with the cut-off removed.
