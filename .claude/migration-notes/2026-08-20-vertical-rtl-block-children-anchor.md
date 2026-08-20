# Vertical rtl block children anchor to the bottom edge

A `writing-mode: vertical-rl`/`vertical-lr` box with `direction: rtl` and block-level children (e.g.
`<div>`/`<p>` children, not plain inline text) previously stacked those children flush against the
box's own physical **top** edge, exactly as `direction: ltr` does. That was wrong per
[CSS Writing Modes 4](https://www.w3.org/TR/css-writing-modes-4/): under `direction: rtl`, a vertical
box's inline-start — the edge its block children should anchor to — is the physical **bottom** edge,
not the top.

Block children of such a box now correctly anchor to the physical bottom edge instead, growing upward
from it. A child shorter than the box's own cross-axis extent now shows its unused space as a gap at
the top, not the bottom. Auto-height and auto-width sizing, block-axis (physical X) stacking order,
and margin summing between siblings are all unaffected — only the cross-axis (physical Y) anchor edge
changed, and only for `direction: rtl`; `direction: ltr` output is byte-identical to before.
