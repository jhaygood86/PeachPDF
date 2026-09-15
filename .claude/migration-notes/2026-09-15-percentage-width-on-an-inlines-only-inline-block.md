# Percentage widths now size inlines-only inline-blocks

Previously, a percentage `width` on a `display: inline-block` whose own content was inline-only was
ignored: the box shrink-to-fit its content as if `width: auto` had been declared. Inline-blocks with
block-level content already resolved the percentage correctly.

Such a percentage now resolves against the containing block's content width and sizes both the room
reserved on the line and the box painted for backgrounds, borders, and overflow clipping. For example,
`width: 50%` inside a 400pt block now produces a 200pt content box.

The repository has no release tags yet; the prior behavior is confirmed by the accepted-gap note and
the two regression fixtures on `main` immediately before this change.
