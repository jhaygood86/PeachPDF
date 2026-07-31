# `:empty`, `:any-link`, and `:scope` are now evaluated

**Landed:** 2026-07-27 (1575b320) — Evaluate the three pseudo-classes a document tree can answer
**Doc section:** docs/html-css-support.md § [Recognized but unmatchable selectors](../../docs/html-css-support.md#recognized-but-unmatchable-selectors)
**Verified against v0.9.6:** this note was not present in the `v0.9.6` tag's docs — confirmed genuine behavior change since 0.9.6, in scope for the next release notes.

`:empty`, `:any-link` and `:scope` used to be in this recognized-but-unmatchable set. They depend solely on the document tree, which a static renderer can read, so they are now [evaluated](#pseudo-classes) — a rule using one of them applies where before it applied to nothing. See [Forward compatibility](#forward-compatibility).
