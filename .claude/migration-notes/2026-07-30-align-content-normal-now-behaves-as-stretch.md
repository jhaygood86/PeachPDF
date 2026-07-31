# `align-content: normal` now behaves as `stretch`

**Landed:** 2026-07-30 (52a43f44) — Fix align-content:normal stretch and column flex break points between items (#565)
**Doc section:** docs/html-css-support.md § [Flexbox](../../docs/html-css-support.md#flexbox)
**Verified against v0.9.6:** this note was not present in the `v0.9.6` tag's docs — confirmed genuine behavior change since 0.9.6, in scope for the next release notes.

`align-content: normal` (the property's initial value — every multi-line flex container that leaves `align-content` unset) used to pack the lines at cross-start and leave the free cross space at cross-end. It now behaves as `stretch`, growing every line equally to fill the container, which is what the spec asks for — a document relying on the old packed-at-start default now needs `align-content: flex-start` written explicitly. Separately, a container with exactly one flex line now sizes that line to the container's own definite cross size whether it got down to one line through `flex-wrap: nowrap` or because a `wrap`/`wrap-reverse` container's content simply fit without wrapping — previously only `nowrap` got this, leaving such a line, and anything stretched against it, sized to content instead. See [Forward compatibility](#forward-compatibility).
