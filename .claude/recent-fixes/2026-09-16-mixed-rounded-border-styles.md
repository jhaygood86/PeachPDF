# Mixed rounded border styles share one corner transition

The rounded border renderer used two incompatible geometry models. Its dedicated `groove`/`ridge`
builder divided a corner arc between adjoining sides according to their width ratio, while the
general path assigned whole arcs to the top and bottom strokes. Relaxing the bevel builder's style
guard alone would therefore have overlapped both renderers at mixed corners.

`TryDrawRoundedBorder` now owns every non-uniform rounded border. Filled styles use side-shaped bands:
one full band for `solid`/`inset`/`outset`, two thirds-separated bands for `double`, and two shaded
halves for `groove`/`ridge`. Dotted and dashed sides follow a centerline through their share of both
corner arcs and are clipped to the matching full-width side band. Every style therefore uses the same
width-ratio split, including suppressed sides and fragment edges.

The old per-edge rounded stroke builder became unreachable and was removed. Uniform solid,
dotted/dashed, and double borders still take the continuous-outline fast path, preserving seamless
fills and whole-perimeter pattern fitting.

What only became obvious after rasterizing the showcase was that clipping a patterned centerline is
necessary even after shortening its arc: a round dot cap can extend past the endpoint into the
neighbor's transition region. The side-shaped clip keeps that cap inside its ownership region.

The same comparison caught a phase-direction mismatch: reusing the fill bands' clockwise traversal
made bottom and left patterns run right-to-left and bottom-to-top. Geometry is unchanged by reversing
a path, but its dash phase is not. Patterned centerlines now follow increasing physical coordinates
on every side (left-to-right or top-to-bottom), matching Chrome's placement of the leftover gap.

Evidence: 74 focused border tests pass on net8.0, the full net8.0 suite passes (11,879 passed,
9 platform skips), and changed production lines have 99.4% diff coverage. The whole solution
rebuilds with zero warnings. The `border_style` showcase contains both combinations; Chrome,
PDFium, and MuPDF agree on their curved transition geometry.
