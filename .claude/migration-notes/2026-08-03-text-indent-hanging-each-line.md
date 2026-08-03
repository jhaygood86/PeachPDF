# `text-indent` now supports `hanging`/`each-line`, and is direction-aware

Previously, `text-indent` only accepted a bare `<length-percentage>` — a declaration using the
`hanging` or `each-line` keywords (CSS Text Module Level 3 §3) failed CSS-OM validation and the whole
declaration was dropped, so an author who wrote `text-indent: 2em hanging` (a common "hanging indent"
bibliography/reference-list style) got no indent at all rather than an error or a partial effect. Both
keywords are now parsed and applied: `hanging` inverts which line(s) of the block get indented (every
line except the first, instead of only the first), and `each-line` additionally indents the line
following every forced break (`<br>`, or a preserved newline under `white-space: pre`/`pre-wrap`/
`pre-line`), not just the block's own first line.

Separately, and not gated behind either keyword: `text-indent` under `direction: rtl` now correctly
insets from the physical right (the line's start side for RTL, per CSS Text 3 §3) for the default-
aligned and `text-align: justify` cases. Previously the indent was always applied on the physical
left regardless of direction, which for RTL's default alignment (`text-align: start` resolving to
`right`) had **no visible effect at all** on a short line and only inconsistent, wrap-boundary-
dependent effects on a long one — this is a correctness fix uncovered while implementing the keywords
above, not a new capability. `text-align: center`, and an RTL box whose `text-align` is explicitly
forced to the opposite side of its writing direction, remain a known gap — see
`.claude/accepted-gaps/text-indent-center-alignment-and-explicit-cross-direction-align.md`.
