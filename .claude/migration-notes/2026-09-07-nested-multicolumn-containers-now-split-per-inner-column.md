# A multi-column container nested inside another one now splits per inner column, not just per outer one

Previously, a multi-column container nested inside another multi-column container split its own children
at the outer container's column boundaries only: whichever inner column a descendant's content actually
landed in, its fragment carried the enclosing outer column's geometry rather than its own — so a decorated
box (a background, a border) inside the inner container's second column could paint at the inner
container's *first* column position instead, since the fragment tree had no way to tell the inner
container's own columns apart once it was already inside an outer one.

A multi-column container nested inside another one now splits its own children per inner column,
independently in each outer column it is filled in — the same per-column fragment behavior a top-level
(non-nested) multi-column container already had.

See [Multi-column Layout](../../docs/html-css-support.md#multi-column-layout) in `docs/html-css-support.md`.
