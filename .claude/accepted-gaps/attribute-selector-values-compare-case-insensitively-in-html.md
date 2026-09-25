# Attribute selector values compare case-insensitively for every HTML attribute

`[data-x=abc]` matches `<p data-x="ABC">`: `CssData.DoesSelectorMatch(Attr*Selector, ...)` compares the value
with the node's `NameComparison`, which is `OrdinalIgnoreCase` for HTML. Selectors 4 §6.3 and the HTML
Standard's "Case-sensitivity of selectors" make the comparison case-sensitive except for a fixed list of
attributes (`type`, `dir`, `align`, `rel`, `method`, ...), so most attributes deviate. Tracked in issue #1384.

The `i`/`s` modifier (`[attr=value i]`, `[attr=value s]`) is implemented and overrides this default either
way, which is what the HTML Standard's own UA rules (`input[type=hidden i]`, `[hidden=until-found i]`) use;
only the *unmodified* default is lenient.

**Why it stays:** making the default spec-exact changes which rules match on existing documents (a selector
that only ever matched because of the lenient comparison would stop), so it needs its own change, the spec's
attribute list, and a migration note - not a rider on the `hidden` attribute fix that surfaced it. The
`docs/html-css-support.md` attribute-selector table states the current behavior.
