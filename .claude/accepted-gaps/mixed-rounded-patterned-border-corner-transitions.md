# Mixed rounded dotted/dashed corners use whole per-edge arcs

When neither end of a dotted/dashed edge is rounded, a corner adjoining a different style, color, or
width is clipped to its unequal-width-aware transition diagonal. If either end is rounded, the
edge uses the older per-edge rounded path for its whole length; that path still assigns complete
corner arcs to the top and bottom edges. Consequently even the opposite square corner is not clipped,
and a final dot/dash can overlap the adjoining edge's corner region or show through a `double` edge's
gap.

CSS Backgrounds and Borders Level 3 requires style transitions to stay within the rounded corner's
transition region. This is therefore a real deviation. No tracking issue can be filed while this
repository has GitHub Issues disabled; file and reference one here if the tracker is enabled later.

Fixing it requires the dotted/dashed stroke and every adjoining rounded style to share the same
curved transition geometry. The straight-sided mitre clip used for square corners cannot express
that region, and merely shortening the stroke loses the corner-following arc.
