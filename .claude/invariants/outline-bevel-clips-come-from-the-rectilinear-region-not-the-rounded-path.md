# A unioned bevel's clips come from the rectilinear region, not from the path it paints

`OutlineRegionPainter.Styled.cs` paints a unioned outline's bevel one contour at a time: for each
piece of the region it fills **that piece's whole band** twice, once per shade, each fill clipped to
the edges of that piece which resolve to that shade. Those clips are built from `BuildContourCorners`
- the region's own right-angled corner points, with each corner's resolved arc radii - and **not**
from the rounded path the band itself is drawn along. Several rules follow, and every one of them has
already been paid for once.

## The clip polygons come from right-angled edges only

A rounded corner is emitted as a `PathSegment.Curve`, which has no single direction of travel and
therefore no shade of its own. Derive the clips from the rounded path and every arc is silently
skipped by the `(dx == 0) == (dy == 0)` zero-length guard - an arc satisfies it, since both deltas are
non-zero. The rectilinear corner points instead give each corner two mitres that meet on its 45
degree diagonal, so the arc is split between the two edges it joins, which is also how
`BoxEdgesDrawHandler.DrawRoundedSideBand` shades a rounded bevelled border on an unfragmented box.

**Symptom if violated:** a `border-radius` + `groove`/`ridge`/`inset`/`outset` outline on a wrapped
inline paints an almost entirely unfilled band with stray triangles where the straight runs were -
grossly broken, not subtly wrong.
`RoundedBevelledOutlineOnAWrappingInlineElement_ShadesTheWholeBandIncludingItsArcs` is the guard; it
was confirmed to fail on all four styles when the clips are taken from the path instead.

## The corner radius is carried in a cap, not along the whole edge

Near a rounded corner the band is an annular sector that deviates from the edge's straight centreline
by up to the corner's radius, so a territory only `halfWidth` deep leaves a wedge of the arc outside
every clip. The reach therefore has to carry the corner radius as well - but **only at the end that
corner is at**. An edge's territory is its straight strip (`halfWidth` either side of the centreline,
run out to the mitre tips) plus one cap per end, reaching `halfWidth + radius` along the edge, and
that reach is taken as the furthest of the layer's three fittings at that corner - the centreline's
and the band's own outer and inner contours, matched by index, since each contour fits its radii to
its own edge lengths.

Carrying the radius along the whole edge instead is not merely wasteful: it hands the edge a
territory that reaches a *different* edge's band wherever two boundary edges pass within
`halfWidth + radius` of each other, which on a wrapped inline is routine - a neighbouring line, a
short staircase step, a pinched neck.

**Symptom if violated (too little reach):** an unpainted wedge at each rounded corner - 5,652px
(PDFium, scale 6) on a `border-radius: 30px`, 6px `inset` outline, against 35px of antialiasing once
fixed. Because a coverage test that only checks the clips' min/max extent is blind to this - the
straight edges' own strips already reach that extent - the guard flattens the band's Béziers and
asserts each sampled point falls inside some clip of its own pass.

**Symptom if violated (reach along the whole edge):** no hole at all, but bands repainted in the
wrong shade: a light sliver along a neighbouring line's outer top edge, a light bar overwriting the
dark run of a staircase notch, a half-light narrow edge.
`BevelledOutline_ShadesEveryBandPixelFromItsOwnEdge` is the guard, and it asserts *ownership* rather
than coverage - the nearest outer straight of the band names a pixel's edge, and a lit-owned pixel
may not fall inside a shaded clip.

## A cap covers the arc's own side of the corner, not both

The arc at a corner is an elliptical quadrant, so it leaves the straight centreline on one side only:
inward for a convex corner, outward for a concave one. A cap therefore grows toward the radius on
that side alone. Covering the other side to the full reach as well would paint the
diagonally-opposite quadrant, which holds no band of this edge - but does hold whatever other edge's
band passes behind the corner within reach. The one exception is a corner the three fittings disagree
about (one inset collapsed its notch into a reversed loop): its shade is genuinely ambiguous there,
so the cap is symmetric and the band stays painted.

**Symptom if violated:** the wrong-shade artifacts above, in corners rather than along runs - both
symmetric caps alone and the order-aware cut below alone leave one of the three repros standing.
This one rests on rasterization, not on the suite: `BevelledOutline_ShadesEveryBandPixelFromItsOwnEdge`
samples ownership along straight runs only, because in the arc zone the two territories meet and
ownership is ambiguous by construction - so widening the caps back to both sides passes every test in
the suite today and has to be caught by rendering the repros through PDFium and MuPDF.

## The lit shade paints first, so only shaded territory is cut back

Both shades fill through the same band path, lit first. A lit territory reaching into a shaded band is
therefore harmless - the shaded fill runs after it and repaints it - while a shaded territory reaching
into a lit band is not. Shaded **caps** are cut back against every lit edge's central strip (axis
rectangles in layout space, subtracted before the wedge clip); lit territories are never cut, and
neither are straight strips of either shade. Cutting cannot punch a hole, because anything cut out of
a shaded cap lay inside a lit strip, which the lit fill paints.

**Symptom if violated:** a dark triangle in a light run - the only direction of over-reach paint order
does not already resolve.

## Each shade is one fill per contour - not one per edge, and not one for the whole region

Consecutive edges frequently resolve to the *same* shade (travelling clockwise, left-then-top are both
`isTopOrLeft`; right-then-bottom are both not). Filling each edge separately abuts two same-coloured
antialiased fills along their shared mitre, which is exactly the artifact
[paint-two-abutting-antialiased-fills-leave-a-seam-along-their-shared-edge.md](paint-two-abutting-antialiased-fills-leave-a-seam-along-their-shared-edge.md)
describes and that `BoxEdgesDrawHandler.TryDrawSeamlessBevel` exists to avoid on the per-side path.
Accumulating a contour's same-shade territories into one clip also drops the draw calls from
O(edges) to 2 per contour (4 for `groove`/`ridge`'s two passes), each re-emitting that contour's band
once rather than once per edge.

It does **not** go one step further and accumulate every *contour* into one clip pair over one band
path holding the whole region, which is what the first implementation did. A clip is a set of
polygons with no notion of which piece it came from, so one piece's caps then select another piece's
band wherever the two pass within reach of each other.

**Symptom if violated (one fill per edge):** a pale diagonal hairline runs out of each corner of the
outline, visible at about 6x zoom against a dark bevel; a pixel histogram of the corner shows a
spread of intermediate colours alongside the face's own.

**Symptom if violated (one fill for the whole region):** two lines of a wrapped inline that are close
but disjoint repaint each other's bands in the wrong shade.
`BevelledOutline_ShadesEveryBandPixelFromItsOwnEdge` guards this structurally rather than by sampling:
every band fill must hold exactly one contour - two subpaths, its outer and inner edge. Sampling alone
does not cover it, because it only sees a piece another piece's clip both reaches and repaints second;
pointing one fill at every contour was confirmed to fail the structural assertion and no other.

## A mitre leans the way its corner turns, and stops where the two mitres cross

Each territory is derived from the edge's own direction and outward normal `(dy, -dx)`, plus **one bit
per end: whether the corner there is convex**. Measuring `s` along the edge and `h` outwards from its
centreline, a convex corner is the outside of the turn, so the band is longer on the outward side and
the mitre opens outwards (`s = -h` at the start, `s = L + h` at the end); a reflex corner is the inside
of the turn and its mitre leans the other way (`s = h`, `s = L - h`). Both edges meeting at a corner
read that same bit, so they still cut it along one shared line and abut exactly - the point is only
that *which* line that is depends on the corner, not on the edge.

It is tempting to conclude it does not: the bisector of a corner is geometrically the same line
whichever way the boundary turns, and a first implementation here leaned every mitre the convex way on
exactly that reasoning. It is wrong, and square-cornered test cases will not show it, because with no
arc to carry the territories meet at the corner point either way.

**Symptom if violated:** on a wrapped inline with `border-radius`, the inner half of every reflex
corner - the notch between two lines - falls in neither edge's clip, drawing as an unpainted diagonal
slash across the band. Measured at 1121px (PDFium, scale 6) on an 8pt `groove` over three lines, against
75px of antialiasing once fixed.

Blink's `ComplexOutlinePainter` reaches the same place by a different route (`MiterSlope` plus inverse
winding); this engine's rule was cross-checked against real Chrome and against both PDFium and MuPDF.

## The clip must stay a simple polygon

Nothing about the two mitres stops them from crossing: they are cut from the edge's own line, and where
they lean towards each other they meet at a finite depth. Past that crossing the edge's territory has
turned inside out, so a rectangle emitted to a fixed depth regardless folds into a **bowtie**, and its
waist cancels under `RFillMode.Nonzero`. Every piece - the straight strip and both caps - is therefore
clipped to the wedge between the edge's own two mitres (`ClipRectToWedge`), which cuts it off at the
crossing, where the territory genuinely ends.

This bites whenever an edge is shorter than twice the mitre's reach - and a cap's reach carries the
corner radius as well as half the width, so a `border-radius` of 12px already exceeds the ~10pt steps
of a wrapped inline's staircase.

**Symptom if violated:** another unpainted diagonal slash, in the same places and for a different
reason - which is why the guard is not a coverage test. A ray cast from a point inside the cancelled
waist still crosses the bowtie's outline an odd number of times, so point-in-polygon coverage reports
it as covered. `BevelledOutlineOnAWrappingInlineElement_KeepsEachClipConvex` asserts on the shape
instead, and was confirmed to fail on all four styles with the wedge clip removed.
