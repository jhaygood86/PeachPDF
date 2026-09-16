# Mixed rounded patterns are fitted over their curved centerline

A patterned side in a non-uniform rounded border used a fixed ideal dash array restarted at its own
corner transition. Chrome instead constructs the complete closed border centerline, fits and phases
the pattern from its top-left top tangent, then clips that stroke to whichever side it is painting.
The hidden distance around earlier sides is therefore load-bearing: changing the box width can move
a right-side dot across the top-right transition even though the right edge itself did not change.
When it straddles the transition it visually closes an adjoining `double` line and leaves one isolated
dot; at another width it moves inward, leaving the line open and two dots isolated.

An unsliced non-uniform border now strokes that complete clockwise center contour once for every
patterned side and clips each stroke to its shared side band. The existing Chrome-derived closed-path
fitter uses the whole measured contour and phase zero. Partial elliptical lengths are measured with
five-point Gauss-Legendre integration. A sliced fragment still uses its open per-side centerline,
because it has no complete physical contour to phase against.

The `border_style` showcase and a direct 240px/245px width sweep reproduce Chrome's width-dependent
corner behavior, including both the one-isolated-dot/closed-corner and
two-isolated-dot/open-corner states. PDFium and MuPDF agree. Tests cover the measured elliptical arc,
closed-contour geometry and phase, device scaling, width-dependent fitting, and the open
sliced-fragment path.
