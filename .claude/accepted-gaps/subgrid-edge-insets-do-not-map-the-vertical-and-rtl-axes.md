# Subgrid edge insets and column contributions are horizontal, left-to-right only

A subgrid's margin, border and padding inset its edge items, and its items size the parent's auto columns, only in a horizontal-writing-mode grid with
`direction: ltr` (`CssLayoutEngineGrid._colStartInset` and friends stay zero otherwise, and `MeasureColumnIntrinsics` skips the subgrid fold-in). A vertical
or `rtl` subgrid keeps the tracks on the parent's lines but its edge items are not inset, as before the [subgrid fix](../recent-fixes/2026-11-12-subgrid-line-names-edges-and-column-contributions.md).
The axes map through `LogicalPropertyResolver` for the rest of the grid engine, so closing it is mapping the start and end edges the same way.

Also left: a subgrid with `grid-template-areas` or column `grid-auto-flow` does not feed the parent's auto columns (its placement needs a layout pass),
and a parent area only partly overlapped by a subgrid gives its `-start`/`-end` names to every spanned line rather than only the first or last (css-grid-2 §9.2).

Tracking issue: #276.
