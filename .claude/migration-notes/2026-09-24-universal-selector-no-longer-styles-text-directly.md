# `*`, `:not()` and `:lang()` no longer style text directly, so more specific rules now win on text

**Landed:** 2026-09-24 — fix for #1360
**Doc section:** docs/html-css-support.md § Selectors (the docs already say `*` matches elements; no wording change needed)
**Verified against v0.9.19:** `git show v0.9.19:src/PeachPDF/Html/Core/CssData.cs` still has `AllSelector => node is { IsRoot: false }`, so the old behavior shipped in v0.9.19 and this is a genuine change for the next release.

Before, a stylesheet such as `* { font-family: arial } h1 { font-family: "Segoe UI" }` rendered heading text
in Arial: the `*` rule was applied to the text inside the `<h1>` directly, beating the heading rule whatever
its specificity or source order. The same happened for inherited properties set by `:not(...)`/`:lang(...)`
rules and for `div * { color: red }` against a `<div>` with no child elements. Now text only inherits from its
element, so the more specific rule wins — matching browsers. Documents that (knowingly or not) relied on a
blanket `*` rule overriding element-specific inherited properties will render differently: usually the
element-specific rule that was being ignored now takes effect.
