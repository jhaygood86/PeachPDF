# Subgrids keep their tracks on the parent's lines and inherit its line names

**Before.** A subgrid with padding or a border had its adopted tracks shifted in by that edge, so its items missed the parent's lines. Items could
not be placed by the parent's line names, a subgrid's items did not size the parent's `auto` columns, and `grid-template: "a" 50px / subgrid` was accepted.

**Now.** The tracks stay on the parent's lines and the edge items are inset instead; the parent's names (and area names) for the spanned lines are
available to the subgrid; a column subgrid's items size the `auto` columns they sit over; `subgrid` after the areas form is invalid.

Part of #276.
