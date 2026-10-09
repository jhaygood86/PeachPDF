# Absolute/fixed children of `display:inline-flex` are positioned and sized

**Before (v0.9.21):** a non-replaced `position:absolute` / `position:fixed` child (a `<div>`…) of an
`inline-flex` container was never laid out — it stayed 0×0 at the page origin and did not paint where its
`top`/`left`/`width`/`height` said. `display:flex` and `display:inline-grid` containers were fine. An
absolute `<img>`/`<svg>` child *did* paint, because it was wrapped in an in-flow block.

**Now:** every absolute/fixed child, replaced or not, is laid out against the container's containing block
like any other flex container's out-of-flow child (css-flexbox-1 §4.1), including when `vertical-align`
moves the container on its line.

**Why:** `inline-flex` reached the flex engine without the step that lays out out-of-flow children. The
`<img>`/`<svg>` case was only broken on unreleased `main` (after
[the blockify change](2026-10-08-inline-level-flex-grid-items-compute-to-block.md) stopped wrapping them),
so for a release-to-release note it is unchanged; only the non-replaced case is a visible change.
