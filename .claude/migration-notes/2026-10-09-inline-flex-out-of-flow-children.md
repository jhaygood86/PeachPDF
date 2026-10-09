# Absolute/fixed children of `display:inline-flex` are positioned and sized

Compared with v0.9.21:

- **Absolute/fixed `<div>` (or other non-replaced) child:** was never laid out — 0×0 at the page origin. Now
  laid out against the container's containing block like any other flex container's out-of-flow child
  (css-flexbox-1 §4.1), also when `vertical-align` moves the container on its line.
- **Absolute `<svg>` child:** was placed ignoring its `top`/`left` (x=36,y=20 for a box that belongs at 87,24).
  Now placed correctly.
- **Absolute `<img>` child:** drawn correctly on release, and still is. It was only broken on `main` after
  [the blockify change](2026-10-08-inline-level-flex-grid-items-compute-to-block.md) stopped wrapping it, so
  release to release it is unchanged — provided the container's `vertical-align` is not `baseline`, which needed
  two further fixes to keep it that way: the image's word no longer registers a rectangle on the container
  (a stray square behind a transparent image, or a missing left/top border), and a generated `::after`/`::before`
  holding text no longer picks up the container's `vertical-align` for its own line.
- **Statically positioned absolute descendant of an atomic inline box** (`inline-block` with a baseline,
  `inline-flex`): now follows the box when `vertical-align` moves it, instead of staying at the pre-alignment
  position (baseline: 20 → 32, Chrome 33; `bottom`: 20 → 30, Chrome 30).
- **In-flow `<img>` in an `inline-flex`:** no longer adds a second rectangle to the container.

**Why:** `inline-flex` reached the flex engine without the step that lays out out-of-flow children.
