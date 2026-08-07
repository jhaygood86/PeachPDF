# `position: sticky` boxes are now placed in normal flow

**Landed:** 2026-08-07 (4def704e) — Place position:sticky boxes in normal block flow
**Doc section:** docs/html-css-support.md § [Display & Layout](../../docs/html-css-support.md#display--layout)
**Verified against v0.9.8:** the `v0.9.8` tag's docs already claimed `sticky` "treated as `relative` in PDF output since there is no scroll," but the actual layout code never placed a sticky box in flow at all — it kept its unlaid-out default location (in practice `(0, 0)`), overlapping other content. Confirmed genuine behavior change since 0.9.8, in scope for the next release notes.

A `position: sticky` box previously rendered at `(0, 0)` (or wherever it happened to sit before layout), regardless of where it appeared in the document. It is now placed exactly as `position: relative` would place it — participating in normal block flow, margin collapsing, and pagination — but with a zero offset, since its `top`/`right`/`bottom`/`left` values are sticky's scroll-threshold parameters (never applied as a static shift) rather than an actual offset. A document relying on the old fallback location will see its sticky content move to its in-flow position.
