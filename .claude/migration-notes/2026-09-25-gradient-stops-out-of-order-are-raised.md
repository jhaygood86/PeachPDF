# A gradient stop positioned before an earlier stop is raised to it

**Before:** a stop with a position lower than an earlier stop's, most commonly the checkerboard idiom
`repeating-conic-gradient(#ddd 0% 25%, #fff 0% 50%)`, either painted nothing (repeating conic) or a soft
blend instead of a hard edge (linear/radial), and produced a shading a strict PDF reader could reject.

**Now:** the stop takes the largest position before it (CSS Images 3 §3.4.3), so the idiom draws the
checkerboard it draws in a browser, and the shading's function bounds are always valid.

Confirmed against `v0.9.19`: the same input rendered blank/blended there.
