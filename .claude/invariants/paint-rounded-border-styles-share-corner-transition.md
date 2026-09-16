# Every style on a non-uniform rounded border must share one corner transition

`BordersDrawHandler.TryDrawRoundedBorder` is the paint boundary for every non-uniform rounded border.
Do not send one style back through an independent per-edge arc builder: CSS Backgrounds and Borders
§4.4 requires color and style transitions to stay in the corner transition region, and two builders
will either overlap or leave a gap there.

The rounded contours and `RoundedCornerAngles` define that shared geometry. Filled styles add their
bands with `AddRoundedBandSide`; dotted and dashed styles follow `AddRoundedSideCenterline` and clip
the stroke to the corresponding full-width side band. The clip is load-bearing for round dots,
because a dot cap extends beyond the centerline into the neighboring transition region.

For an unsliced box, each patterned side must stroke the complete clockwise center contour, starting
at the top-left top tangent, and then rely on that side's clip. The pattern is fitted to and phased
from the whole closed contour, not restarted at the side's transition. Consequently, changing the
box width can move a dot across the top-right transition even though the right edge itself did not
change: the dot merges with the adjacent band at one width and leaves it open at another. An open
per-side centerline remains appropriate only for a sliced fragment that omits a physical edge; keep
those paths left-to-right or top-to-bottom because reversing them changes the visible dash phase.

Uniform solid, dotted/dashed, and double borders are the intentional exception: they have no style,
color, or width transition, so `TryDrawUniformBorder` may paint one continuous outline around the
whole box.
