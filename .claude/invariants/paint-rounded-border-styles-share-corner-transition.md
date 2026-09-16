# Every style on a non-uniform rounded border must share one corner transition

`BordersDrawHandler.TryDrawRoundedBorder` is the paint boundary for every non-uniform rounded border.
Do not send one style back through an independent per-edge arc builder: CSS Backgrounds and Borders
§4.4 requires color and style transitions to stay in the corner transition region, and two builders
will either overlap or leave a gap there.

The rounded contours and `RoundedCornerAngles` define that shared geometry. Filled styles add their
bands with `AddRoundedBandSide`; dotted and dashed styles follow `AddRoundedSideCenterline` and clip
the stroke to the corresponding full-width side band. The clip is load-bearing for round dots,
because a dot cap extends beyond its centerline endpoint even when the path itself ends exactly at
the transition.

Uniform solid, dotted/dashed, and double borders are the intentional exception: they have no style,
color, or width transition, so `TryDrawUniformBorder` may paint one continuous outline around the
whole box.
