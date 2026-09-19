# Bevel territory (s, h) <-> layout mapping must invert exactly in every branch

`BevelEdge.ToLayout`/`FromLayout` convert corner-cap rectangles between an edge's
`(s, h)` frame (along the edge, outwards from its centreline) and layout coords.
All four travel directions are axis rotations/reflections of each other, so each
of the eight min/max branches must be the exact inverse of its counterpart: the
minimum in one frame is the minimum *or the maximum* in the other depending on
the sign of the direction component (`h0 = Start.Y - rect.Y1` when `Ny < 0`,
because `h` decreases as `y` increases there).

Measured symptom of getting one branch wrong: every territory piece of every
edge travelling that direction comes back degenerate from the round trip and is
silently skipped - an entire shade direction vanishes from the outline (here all
east-travelling edges, whose lit bands simply never painted). It cost real
debugging time because nothing throws: the pieces are dropped by the same
degenerate-rectangle guard that legitimately filters slivers, and most tests
still pass - only a per-clip subpath-count assertion noticed. Any change touching
these branches must be verified against all four travel directions, not just the
staircase under test.
