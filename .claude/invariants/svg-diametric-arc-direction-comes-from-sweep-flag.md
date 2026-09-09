# Diametric SVG arc direction comes from `sweep-flag`

When an elliptical arc's endpoints are diametrically opposite, its angular magnitude is exactly
180 degrees. The `large-arc-flag` cannot distinguish the two candidate arcs because neither is
larger than the other; only `sweep-flag` selects the side.

After endpoint-to-center conversion, normalize the signed sweep directly from `sweep-flag`.
Do not infer direction from a strict less-than-180 comparison or from endpoint ordering. The
measured failure mode is a two-arc circle whose halves both render on the same side.
