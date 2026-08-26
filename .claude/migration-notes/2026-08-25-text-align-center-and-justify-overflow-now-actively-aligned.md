# Overflowing `text-align: center`/`justify` lines are now actively aligned

**Landed:** 2026-08-25 — `CssLayoutEngine.ApplyCenterAlignment`/`ApplyJustifyAlignment` shared
`ApplyRightAlignment`'s pre-fix overflow guard
**Doc section:** docs/html-css-support.md § [Text Layout](../../docs/html-css-support.md#text-layout)

An overflowing `text-align: center` line (a `white-space: nowrap` line, or one unbreakable token,
wider than its container) previously stayed at its natural, always-left-to-right-flowing position
instead of being centered - it rendered flush-left, spilling only past the right edge, the same way
left-aligned content would. It now spills symmetrically past *both* edges, matching real browsers.

A `text-align: justify` line that overflows because it holds a nested `white-space: nowrap` run of
more than one word (a phrase or name kept together with a nested `nowrap` span, not just a single
unbreakable token) previously could render with its last word forced backward into the earlier
word's own trailing edge - overlapping, garbled text. It now keeps its words in their natural,
non-overlapping order and lets the line spill past the container's edge coherently. A justified line
holding a single overflowing word was already positioned correctly (flush against the target edge)
and is unaffected by this change.

A document relying on either of these overflow shapes rendering at their pre-fix (incorrect, natural/
flush-left) position will see a visual change; correctly-fitting `center`/`justify` content is
unaffected.

A vertical-writing-mode (`writing-mode: vertical-rl`/`vertical-lr`) `text-align: justify` column
had the identical unconditional-`spacing`/unconditional-last-word-flush shape and is fixed the same
way.
