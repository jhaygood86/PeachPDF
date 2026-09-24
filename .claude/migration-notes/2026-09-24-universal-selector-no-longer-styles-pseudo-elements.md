# A bare `*` (and `:not()`/`:lang()`) no longer styles `::before`/`::after`/`::marker` boxes

**Landed:** 2026-09-24 — fix for #1361
**Doc section:** docs/html-css-support.md § Selectors (already describes `*` as matching elements; no wording change needed)
**Verified against v0.9.19:** `git show v0.9.19:src/PeachPDF/Html/Core/CssData.cs` still has `AllSelector => node is { IsRoot: false }`, so the old behavior shipped in v0.9.19.

Before, `* { margin: 0; padding: 0 }` (or `box-sizing`, `display`, borders) also applied to generated
`::before`/`::after`/`::marker`/`::first-letter` boxes. Now it applies to elements only, as in browsers:
generated content needs its own `*::before, *::after { ... }` rule. Inherited properties are unaffected.
