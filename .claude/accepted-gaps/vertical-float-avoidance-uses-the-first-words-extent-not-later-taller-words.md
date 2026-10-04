# Vertical float avoidance uses the first word's extent, not later taller words

Tracked by [#1623](https://github.com/jhaygood86/PeachPDF/issues/1623).

A vertical-writing column is tested against floats over its prospective block-axis span: the larger
of the strut's `line-height` and the first word's line-box extent (CSS 2.1 §10.8.1). A later, taller
word in the same column grows the column after its inline span was chosen, so the grown span can
reach a float the original span cleared. Fixing it means re-evaluating the span when the thickness
grows and moving the column, which the first-word estimate deliberately does not attempt.
