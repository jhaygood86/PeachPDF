# Definite-height backgrounds and borders no longer expand to overflowing content

A box with a definite CSS `height` now paints its background and borders at that specified height
even when a taller child overflows it. Previously the layout box had the correct height, but fragment
materialization silently enlarged its painted decoration to cover the child.

Overflowing content remains visible when `overflow` permits it; only the parent box's own background
and border stop at the specified edge.
