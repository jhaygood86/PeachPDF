# The universal selector, `:not()` and `:lang()` no longer match generated pseudo-element boxes (#1361)

Follow-up to [the text-box fix](2026-09-24-universal-selector-does-not-match-text.md) (#1360), which had
left `CssBox { IsPseudoElement: true }` matchable. Selectors 4 §5.2 makes `*` any *element*; a
`::before`/`::after`/`::marker`/`::first-letter`/`::placeholder`/footnote box is reached only through its own
pseudo-element selector on the originating element, so `* { margin: 0 }` must not apply to generated content.

**Fix:** `CssData.IsElementNode` is now just `TagName is not null`. This is safe because a pseudo-element
rule never matched the box through `*` in the first place: `DoesSelectorMatch(CompoundSelector, ...)` matches
every other compound member (`*`, `:not(...)`, classes, ...) against the `referenceBox` — the originating
element — and only asks the box itself for the pseudo-element selector. So `*::before`, `.charts-css
*::before`, `div:not(.x)::before`, `*::footnote-call` all still work; only the bare, subject-position `*`
that ran against the generated box itself is gone.

**Trap:** `* { ... }` used to reach generated boxes for *non-inherited* properties (`margin`, `padding`,
`box-sizing`, `display`, borders). Inherited ones were unaffected either way, since the box inherits from its
parent. Anything relying on a blanket `*` reset also resetting generated content now needs `*::before,
*::after` — as it does in browsers.

**Evidence:** `UniversalSelectorTextNodeIntegrationTests` pseudo-element tests (fail before, pass after);
full net8.0 suite; showcases/Acid2/Charts.css-style fixtures re-checked.
