# Rounded groove and ridge borders retain their bevel

Previously, a `groove` or `ridge` border with `border-radius` degraded to one solid, full-width
stroke and lost its two-tone beveled appearance. It now follows the rounded corners as two
half-width bands, including when side widths/colors differ, `groove` and `ridge` are mixed, or a
sliced fragment omits an edge. At a shared corner the wider adjoining edge owns proportionally more
of the curve.
