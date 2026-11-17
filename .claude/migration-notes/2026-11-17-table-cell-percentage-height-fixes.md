# Table cell percentage-height fixes

**Before / now**, each a change a document author can see:

- **A percentage `height` that cannot be resolved** (its containing block's own height is indefinite,
  e.g. `height: 50%` with no ancestor height to resolve it against, and no preferred `aspect-ratio` to
  size it instead) used to skip `min-height` entirely and leave the box at its unclamped content height.
  `min-height` is now honored even then, so a scrollable box styled `height: 100%; min-height: 100px`
  inside a cell whose own height is likewise unresolvable now grows to at least 100px instead of
  collapsing to its (possibly empty) content.
- **A table cell's own percentage `height`** (`td { height: 100% }`) used to resolve directly against the
  table when the table's height was otherwise definite, inflating the cell (and so its row) to that full
  table height. It is now treated as `auto` for sizing the cell itself, per css-tables-3 — the row is
  sized the ordinary way instead, so a percentage-height cell no longer doubles (or keeps compounding) a
  table's real height.

See [Tables](../../docs/html-css-support.md#tables).
