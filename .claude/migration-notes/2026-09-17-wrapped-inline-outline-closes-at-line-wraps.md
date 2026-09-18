# A wrapped inline's outline now closes on every line

Previously, an inline element whose `outline` wrapped across multiple lines left its two line-wrap
edges open: only the first line's leading edge and the last line's trailing edge were drawn, leaving a
visible gap at every internal line break — `box-decoration-break: slice`-style geometry, even though
`box-decoration-break` itself does not govern outline at all.

Every line now closes into its own complete, independently rounded ring instead (Chromium's own
"closed rect per line" shape), matching CSS Basic User Interface 4's recommendation that a fragmented
outline be fully connected rather than open on some sides. Border and background are unaffected: their
own line-wrap edges stay open, exactly as `box-decoration-break: slice` requires for those properties.

A page or column break is a different axis and is unaffected by this change either: the block-axis
edge a page or column break cuts through still stays open on both sides of it.

Scoped to `horizontal-tb` writing mode (and `sideways-rl`/`sideways-lr`, whose line boxes lay out the
same way) for now — a `vertical-rl`/`vertical-lr` wrapped inline's outline keeps its previous open-edge
geometry, tracked alongside the pre-existing gap in reserving that axis's own border/padding inset (see
`.claude/accepted-gaps/no-vertical-writing-mode-layout.md`).
