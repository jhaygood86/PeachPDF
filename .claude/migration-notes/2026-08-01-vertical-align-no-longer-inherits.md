# `vertical-align` no longer inherits from an ancestor

**Landed:** 2026-08-01 — Stop cascading vertical-align as inherited (issue #530)
**Doc section:** docs/html-css-support.md § [Color & Typography](../../docs/html-css-support.md#color--typography) (the `vertical-align` row)
**Verified against v0.9.7:** the `vertical-align` row in the `v0.9.7` tag's docs describes the same
supported value set with no mention of inheritance — confirmed genuine behavior change since 0.9.7,
in scope for the next release notes.

A descendant with no explicit `vertical-align` used to pick up whatever value an ancestor declared,
even though CSS 2.1 §10.8.1 defines the property as `Inherited: no`. It now correctly resolves to the
initial value `baseline` instead, matching browsers. The most visible symptom of the old behavior was
`vertical-align: unset` acting like `inherit` (copying the parent's value) rather than `initial`
(resetting to `baseline`) — `unset` now does the latter, per spec. A document that relied on the old
inheriting behavior — declaring `vertical-align` once on a container and expecting it to apply to
every descendant inline box — needs to declare it explicitly on each element that should be affected,
or use `vertical-align: inherit` to opt in where inheritance is actually wanted.
