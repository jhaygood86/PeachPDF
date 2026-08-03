# `@supports` now actually evaluates its condition, instead of always dropping the block

**Landed:** 2026-08-02 — Wire real `@supports` evaluation onto the CSS/SVG property registry generator
**Doc section:** docs/html-css-support.md § [CSS At-Rules](../../docs/html-css-support.md#css-at-rules) (the `@supports` row)
**Verified against v0.9.8:** the `v0.9.8` tag's `docs/html-css-support.md` `@supports` row reads "The
whole block is ignored — PeachPDF has no feature-query engine... it drops them (the inner rules never
apply, whether the condition is affirmative or `not (…)`)" — confirmed genuine behavior change since
0.9.8, in scope for the next release notes.

Previously, **every** `@supports (...) { ... }` block was unconditionally ignored regardless of its
condition — an affirmative block (`@supports (display: flex) { ... }`) never applied, and a `not`-guarded
fallback (`@supports not (display: flex) { ... }`) never applied either. This was a deliberate interim
stopgap (tracking issue [#283](https://github.com/jhaygood86/PeachPDF/issues/283)): applying rules guarded
by a condition the renderer couldn't test would have let a `not`-guarded fallback and its "enhanced"
counterpart both apply at once, which is worse than dropping both.

`@supports`'s condition is now genuinely evaluated — once, at stylesheet-indexing time, since the
condition (unlike a `@media` feature query) takes no arguments and can't vary per element or query — against
the actual set of HTML/SVG properties PeachPDF's renderer implements (via the generated
`CssPropertyRegistry`/`SvgPropertyRegistry`, the same dispatch the cascade itself uses). A document that
previously worked around the always-drop behavior — for example, by putting styles that must always render
outside any `@supports` block, or by never relying on a `not`-guarded fallback actually taking effect — will
now see `@supports`-guarded content apply or not apply based on whether PeachPDF genuinely supports the
guarded declaration, matching real browser behavior far more closely. In particular:

- `@supports (display: flex) { ... }`-style blocks guarding features PeachPDF renders now correctly apply.
- `@supports (animation-name: ...) { ... }` / `@supports (transition: ...) { ... }`-style blocks guarding
  features PeachPDF's CSS-OM parses but never renders now correctly do **not** apply — this was previously
  a silent false positive (the old fallback oracle only checked "does this parse," not "does this render").
- A `not`-guarded fallback for a genuinely-unsupported feature now correctly applies where it previously
  never did.

One known, narrow gap: a handful of presentation properties PeachPDF renders through hand-written dispatch
rather than the generated registry — `mask`, `marker`/`marker-start`/`marker-mid`/`marker-end`, and
`clip-path` on SVG elements — aren't yet covered by the `@supports` oracle and will report unsupported
despite working. `@container` is unaffected by this change and remains always-ignored (its condition
depends on live layout, not a static parse-time fact, so it still can't be evaluated).
