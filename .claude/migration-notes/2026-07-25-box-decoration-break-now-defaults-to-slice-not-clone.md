# `box-decoration-break` now defaults to `slice`, not `clone`

**Landed:** 2026-07-25 (edc00df8) — Honour box-decoration-break at page and line breaks
**Doc section:** docs/html-css-support.md § [Decorations at a break](../../docs/html-css-support.md#decorations-at-a-break)
**Verified against v0.9.6:** this note was not present in the `v0.9.6` tag's docs — confirmed genuine behavior change since 0.9.6, in scope for the next release notes.

A wrapping inline box whose background is a gradient, or which has `border-radius` or a `box-shadow`, used to be painted independently on each line — a separate full gradient, its own four rounded corners, and a shadow along every wrap. That is `clone`'s behavior, and it was applied whatever the declared value was. Such a box now renders as `slice` unless `box-decoration-break: clone` is set, which is both the spec's initial value and what browsers draw. Add `box-decoration-break: clone` to keep the previous appearance. See [Forward compatibility](#forward-compatibility).
