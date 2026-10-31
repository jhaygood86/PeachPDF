# `justify-items` has the initial value `normal`, not `legacy`

CSS Box Alignment 3 gives `justify-items` the initial value `legacy` (`legacy` also takes a `left`/`right`/`center`
companion keyword, and is what makes a block container pass its own `text-align: -webkit-*` alignment down to
`justify-self: auto` children). `css-properties.json` declares `normal` and `JustifyItem` has no `legacy` member.

Left alone deliberately: in a flex or grid container `legacy` behaves as `normal` (stretch), which is the only place
this engine reads `justify-items`, so the declared initial is indistinguishable in layout. Closing the gap means a new
`JustifyItem.Legacy` member, the `legacy [left|right|center]` grammar in `Map.JustifyItemKeywords`, the block-layout
`justify-self: auto` consumer, and an inheritance rule for the `legacy` companion keyword - none of it observable until
block-level `justify-self` is implemented.

Found by the initial-value audit that accompanied generating the CSS-OM property classes from `css-properties.json`
(see `.claude/migration-notes/2026-10-31-spec-inheritance-and-initial-fixes.md`). Tracking issue: to be filed.
