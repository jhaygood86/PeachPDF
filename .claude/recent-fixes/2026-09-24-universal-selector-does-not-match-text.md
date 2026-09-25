# The universal selector, `:not()` and `:lang()` no longer match bare text boxes (#1360)

`AllSelector => node is { IsRoot: false }` matched the anonymous box a raw text node becomes, so a
`* { font-family }` / `* { color }` rule landed on the text *directly* and no more specific rule for the
parent element (any specificity, any source order) could reach it. Cascade 4 §1.1: text nodes "cannot be
targeted by selectors", all their values come from inheritance; Selectors 4 defines `*`, `:not()` and
`:lang()` over elements.

**Fix:** `CssData.IsElementNode(node)` = has a `TagName`. Applied in
`AllSelector`, `NotSelector`, `LangSelector` only — the other selectors already reject non-elements
(type/class/id/attribute have nothing to match; every structural pseudo-class checks `TagName is null`
and filters siblings by `TagName`, so `:nth-child`'s default `Kind = AllSelector` was already element-only).

**Traps:**
- This fix first kept generated pseudo-element boxes matchable; #1361 then removed that too — see
  [the pseudo-element follow-up](2026-09-24-universal-selector-does-not-match-pseudo-elements.md).
- The `box.HtmlTag is null` guard in `DoesSelectorMatch(CompoundSelector, ...)` (the `*::before` text-box
  hang, `UniversalPseudoElementIntegrationTests`) is now mostly redundant but stays as a last line of defence.
- `FontFamily` collapses to the first *installed* family, so a test asserting a Segoe UI stack must compare
  `FontFamilyList`, not `FontFamily`.

**Evidence:** `UniversalSelectorTextNodeIntegrationTests` — the 5 negative tests fail on the unfixed code
and pass with it; full net8.0 suite and diff coverage run before merge.
