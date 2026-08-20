# Vertical writing-mode block children now collapse margins

A `writing-mode: vertical-rl`/`vertical-lr` box's block-level children (e.g. `<div>`/`<p>` children, not
plain inline text) previously had their adjoining margins **summed** along the block axis: two
block-axis-stacked siblings with `10pt` and `10pt` margins facing each other left a `20pt` gap between
them. That was wrong per [CSS 2.1 §8.3.1](https://www.w3.org/TR/CSS21/box.html#collapsing-margins): the
correct gap is `10pt` (the larger margin), not the sum of both.

Adjoining margins between block-axis-stacked children now collapse correctly — the larger positive
margin, or (for a mixed- or all-negative set) the max positive plus the min negative. A document that
relied on the old (unintentional) summed gap will now see a smaller gap between such children. A
self-collapsing empty child between two real siblings folds its own margins into the same shared group
rather than acting as two separate pairs.

Separately, when a vertical box is itself nested inside another vertical box with the same
`writing-mode`, its own block-start/block-end margin can now genuinely collapse with its first/last
stacked child's margin (when the outer box has no border/padding on that edge) — the child sits flush
against the outer box's own content edge, and the outer box's own effective margin/size reflects
whichever value is larger. This only applies to nested same-axis vertical composition; a vertical box
embedded directly in ordinary `horizontal-tb` flow is unaffected (the two axes are unrelated, not
"blocked").
