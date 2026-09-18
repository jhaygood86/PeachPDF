# `text-decoration-skip-ink` now applies to a rotated run under a vertical writing mode

Previously, `text-decoration-skip-ink` (the default `auto`, and the explicit `all`) never interrupted
an underline or overline anywhere under a true vertical writing mode (`vertical-rl`/`vertical-lr`) -
the decoration line was always drawn as one unbroken stroke, straight through any glyph ink it crossed,
regardless of the writing mode's text orientation.

It now interrupts correctly for a **rotated** (sideways) run - one ordinary horizontal glyph run
reoriented as a whole (the shape `text-orientation: sideways`, or the default `mixed`'s own
classification, gives most non-CJK scripts). The ink band is measured in that run's own pre-rotation
frame and the resulting gaps mapped back into the physical column, the same interruption behavior
`auto`/`all` already provide under a horizontal writing mode.

An **upright** run (each character stacked individually down the column, with no single natural
horizontal layout to reduce to) is unaffected and still draws its decoration unbroken - this half of
the gap remains open, tracked as part of
[#1145](https://github.com/jhaygood86/PeachPDF/issues/1145)'s own accepted-gap note. The atomic-inline
exclusion (an atomic inline is not decorated) also remains unaffected under a vertical writing mode,
for either run kind.
