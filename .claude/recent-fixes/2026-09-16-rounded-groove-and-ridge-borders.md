# Rounded groove and ridge borders no longer flatten to a solid stroke

The rounded-border path was stroke-based, so it could express only one width and color. A uniform
`groove`/`ridge` needs two bands and changes shade by side, which made it fall through to a single
solid stroke.

The dedicated path now fills curved per-edge bands for each half of the border. Each contour uses
the four sides' own widths; shared corner arcs are divided by the adjoining width ratio, and an edge
omitted by fragmentation produces an open square end rather than a false rounded corner. Sides with
the same resolved shade are included in one path rather than painted as abutting fills, avoiding a
pale antialiasing seam.

A follow-up generalized the corner-transition model to every rounded border style, so a
`groove`/`ridge` edge now keeps both bands beside `solid`, `double`, or a patterned style. See
[mixed rounded border styles](2026-09-16-mixed-rounded-border-styles.md).

Evidence: all 63 focused border-style paint tests pass on net8.0, changed production lines have
90.33% diff coverage, and the whole solution rebuilds with zero warnings. The updated showcase was
compared directly with Chrome and rasterized through PDFium and MuPDF; all three show the same
curved bevel geometry (the two PDF renderers' mean per-channel difference is no greater than 1.506,
attributable to antialiasing).
