# `break-before`/`break-after: column` and `break-inside: avoid-column` now affect multi-column layout

**Landed:** 2026-07-26 (a47c1467) — Honor break values that name the column fragmentation context
**Doc section:** docs/html-css-support.md § [Multi-column Layout](../../docs/html-css-support.md#multi-column-layout)
**Verified against v0.9.6:** this note was not present in the `v0.9.6` tag's docs — confirmed genuine behavior change since 0.9.6, in scope for the next release notes.

`break-before: column`, `break-after: column` and `break-inside: avoid-column` previously parsed and cascaded with no layout effect at all. They now change where content lands inside a multi-column container, so a document that already carries them — a stylesheet written for a browser, most likely — gains column breaks it did not have before. Their behavior outside a multi-column container is unchanged: still nothing. See [Forward compatibility](#forward-compatibility).
