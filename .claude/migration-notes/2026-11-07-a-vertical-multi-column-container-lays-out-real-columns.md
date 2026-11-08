# A multi-column container under a vertical writing mode lays out real columns

**Before:** `column-count`/`column-width` were inert under `writing-mode: vertical-rl`/`vertical-lr`. The container
fell back to a single column, and its block-level children were stacked top to bottom (the horizontal block flow)
instead of along the block axis, so even a `column-count: 1` vertical container laid its paragraphs out in the wrong
direction.

**Now:** the columns are horizontal strips stacked down the inline axis (bottom to top under `direction: rtl`),
the content of each strip flows along the block axis (right to left for `vertical-rl`, left to right for
`vertical-lr`) and continues in the next strip, `column-gap` and `column-rule` (a horizontal line) apply between the
strips, `column-fill: balance` shrinks an auto `width` to the balanced block size, and `column-span: all`,
`break-before`/`break-after: column`, `break-inside: avoid` and `orphans`/`widows` apply. A vertical container with
`column-count: 1` stacks its children along the block axis. A document that set `column-count` on a vertical
container (and so relied on it being ignored) will see its content divided into strips.

A vertical box is still moved whole to the next page when it does not fit, and a vertical multi-column container is
no longer resumable across a page boundary (its strips are all inside one page band).
