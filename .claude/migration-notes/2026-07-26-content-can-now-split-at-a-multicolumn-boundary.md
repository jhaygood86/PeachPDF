# Content can now split at a multi-column boundary

**Landed:** 2026-07-26 (4e83cdf3) — Give a fragment geometry of its own, so a child splits across a column
**Doc section:** docs/html-css-support.md § [Multi-column Layout](../../docs/html-css-support.md#multi-column-layout)
**Verified against v0.9.6:** this note was not present in the `v0.9.6` tag's docs — confirmed genuine behavior change since 0.9.6, in scope for the next release notes.

Before content could split at a column boundary, a child was atomic per column — a paragraph that did not fit moved to the next column whole, and one too tall for any column overflowed it. Documents relying on that (a container sized so that exactly one child fit per column, say) may now paginate differently, because the boundary is a real break point rather than a whole-child packing decision. See [Forward compatibility](#forward-compatibility).
