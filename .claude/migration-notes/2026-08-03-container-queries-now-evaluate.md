# `@container` now actually evaluates, instead of always dropping the block

**Landed:** 2026-08-03 — Implement CSS Container Queries (CSS Containment 3)
**Doc section:** docs/html-css-support.md § [CSS At-Rules](../../docs/html-css-support.md#css-at-rules) (the
`@container` row) and the new § [CSS Container Queries](../../docs/html-css-support.md#css-container-queries)

Previously, every `@container { ... }` block was unconditionally ignored — its condition (whether a
size-feature form like `@container (min-width: 300px)` or, once parsing existed for it, a `style()`
form) was never evaluated, so the inner rules never applied regardless of whether the condition would
have been true. This was a deliberate stopgap (tracked at old issue #284): unlike `@supports` (a
static, parse-time-decidable fact), a container query's truthiness depends on the nearest matched
element's own post-layout size, which a single-pass cascade-then-layout pipeline has no way to know
before cascade runs. `container-type`/`container-name` were parsed at the CSS-OM level but never
wired onto the box tree, so no element could actually be recognized as a query container either.

Both are now genuinely supported:

- **`container-type: size | inline-size | normal`** and **`container-name`** (plus the `container`
  shorthand) are cascaded onto every element, establishing size and/or named query containers.
- **Size queries** (`@container (min-width: 300px) { ... }`) evaluate against the nearest eligible
  ancestor's real resolved content-box size, via a bounded convergence loop that re-cascades and
  re-lays-out the document (up to 4 total passes) whenever a size container's resolved size changes
  pass-over-pass — see `HtmlContainerInt.PerformLayout`.
- **`style()` queries** (`@container style(--theme: dark) { ... }`) evaluate against the nearest
  eligible ancestor's own resolved style (any `container-type`, including `normal`, qualifies — a
  style query needs no layout containment), including `and`/`or`/`not` combinators.
- **Container-relative units** `cqw`/`cqh`/`cqi`/`cqb`/`cqmin`/`cqmax` now parse and resolve against
  the nearest ancestor query container's own size (falling back to `0` with no eligible container,
  same as the existing `vw`/`vh`/`vmin`/`vmax` fallback).

A document that previously worked around the always-ignored behavior — for example, by putting
`@container`-guarded styles' content directly at top level, or by relying on `container-type`/
`container-name` having no observable effect — will now see `@container` blocks apply or not apply
based on the real evaluated condition, matching real browser behavior. In particular, a document using
a CSS framework whose responsive components are built on container queries (rather than media
queries) will now render those components' container-relative breakpoints correctly instead of always
falling back to their base/no-match styling.

Not yet supported: `scroll-state()` queries (a different, newer module - CSS Containment 4). A
`style()` query's value comparison is a trimmed literal-text match against the container's resolved
value, not full computed-value equivalence — see the `style()` row in
[CSS Container Queries](../../docs/html-css-support.md#css-container-queries) for the exact boundary.
