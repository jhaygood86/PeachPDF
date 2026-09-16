# Rounded groove and ridge borders retain their bevel

Previously, a `groove` or `ridge` border with `border-radius` degraded to one solid, full-width
stroke and lost its two-tone beveled appearance. The same fallback still occurred after the first
rounded-bevel implementation whenever another visible side used a different style. It now follows
the rounded corners as two half-width bands, including when side widths/colors differ, any other
border styles share the box, or a sliced fragment omits an edge. At a shared corner the wider
adjoining edge owns proportionally more of the curve.
