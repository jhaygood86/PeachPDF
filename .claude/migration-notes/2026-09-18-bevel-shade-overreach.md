# Bevelled outlines on wrapped inlines no longer repaint bands in the wrong shade

## Before

A bevelled outline (`inset`, `outset`, `groove`, `ridge`) with a `border-radius`
on a wrapped inline could paint slivers and bars in the wrong shade wherever one
boundary edge of the unioned region passed within about half the outline width
plus the corner radius of another edge: a light sliver along a neighbouring
line's outer top edge, a light bar overwriting the dark run of a staircase
notch, a half-light narrow edge. Each boundary edge's shade territory was one
quad carrying the full `halfWidth + max(corner radius)` reach along the whole
edge, so the lit and shaded fills (painted through the same band path, shaded
second) repainted any band pixel inside another edge's over-wide clip.

## Now

Each edge carries half the width along its straight run and grows toward the
radius only in corner caps on the arc's own side; shaded caps are additionally
cut back against lit straight runs; and each disjoint piece of the region gets
its own band fill, so edges cannot reach across contours. The artifacts above
are gone; what remains at shade boundaries are the ordinary one-pixel mitre
transitions, matching the per-line rings.

## Why

This restores the behavior the unioned bevels were specified against (main's
per-line rings and Chrome): a band pixel's shade comes from its own edge.
