# `display: grid-lanes` is supported; `grid-auto-flow` accepts `normal`

Before: `display: grid-lanes` / `inline-grid-lanes` were dropped as unknown values and the container laid out as a block. Now they lay out
as [Grid Lanes](../../docs/html-css-support.md#grid-lanes). `fit-tolerance` (and its earlier name `flow-tolerance`), `grid-lanes-direction`
and `grid-lanes-pack` are new properties.

`grid-auto-flow` now accepts `normal` and its initial value (as read back from the CSS-OM) is `normal`, not `row`; a grid lays out the
same, since it reads `normal` as `row`. Confirm against `git show <previous-tag>:docs/html-css-support.md` when cutting release notes.

The two-keyword `display` syntax now works for the pairs with a single-keyword equal: `display: inline flex` was dropped as invalid before and
is now `inline-flex` (likewise `block grid`, `inline grid`, `inline flow-root`, `inline table`, `inline grid-lanes`). A page that wrote it
and relied on the declaration being ignored will lay out differently.
